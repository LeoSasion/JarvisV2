import {
  createAssistantMessageEventStream,
} from "@earendil-works/pi-ai";
import { randomUUID } from "node:crypto";
import { once } from "node:events";
import { createConnection } from "node:net";

const brokerProtocol = "jarvisv2-pi-model-broker-v1";
const brokerProviderId = "jarvis-desktop-broker";
const brokerModelId = "desktop-default";
const maximumBrokerFrameBytes = 1_048_576;
const pipePattern =
  /^\\\\\.\\pipe\\jarvis2-pi-model-[0-9a-f]{32}$/iu;

function emptyUsage() {
  return {
    input: 0,
    output: 0,
    cacheRead: 0,
    cacheWrite: 0,
    totalTokens: 0,
    cost: {
      input: 0,
      output: 0,
      cacheRead: 0,
      cacheWrite: 0,
      total: 0,
    },
  };
}

function createOutput(model) {
  return {
    role: "assistant",
    content: [],
    api: model.api,
    provider: model.provider,
    model: model.id,
    usage: emptyUsage(),
    stopReason: "stop",
    timestamp: Date.now(),
  };
}

function boundedInteger(value) {
  return Number.isSafeInteger(value) && value >= 0
    ? value
    : 0;
}

function applyUsage(output, usage) {
  output.usage.input = boundedInteger(usage?.input);
  output.usage.output = boundedInteger(usage?.output);
  output.usage.cacheRead = boundedInteger(usage?.cacheRead);
  output.usage.cacheWrite = boundedInteger(usage?.cacheWrite);
  output.usage.totalTokens =
    output.usage.input +
    output.usage.output +
    output.usage.cacheRead +
    output.usage.cacheWrite;
}

async function writeRecord(socket, record) {
  const line = JSON.stringify(record);
  if (Buffer.byteLength(line, "utf8") > maximumBrokerFrameBytes) {
    throw new Error("Desktop model broker request exceeded its frame limit.");
  }
  if (!socket.write(`${line}\n`, "utf8")) {
    await once(socket, "drain");
  }
}

async function* readRecords(socket) {
  socket.setEncoding("utf8");
  let buffer = "";
  for await (const chunk of socket) {
    buffer += chunk;
    let newlineIndex = buffer.indexOf("\n");
    while (newlineIndex >= 0) {
      const line = buffer.slice(0, newlineIndex);
      buffer = buffer.slice(newlineIndex + 1);
      newlineIndex = buffer.indexOf("\n");
      if (line.length === 0) {
        continue;
      }
      if (
        Buffer.byteLength(line, "utf8") >
        maximumBrokerFrameBytes
      ) {
        throw new Error(
          "Desktop model broker response exceeded its frame limit.",
        );
      }
      yield JSON.parse(line);
    }
    if (
      Buffer.byteLength(buffer, "utf8") >
      maximumBrokerFrameBytes
    ) {
      throw new Error(
        "Desktop model broker pending frame exceeded its limit.",
      );
    }
  }
  if (buffer.trim().length !== 0) {
    throw new Error(
      "Desktop model broker closed with an incomplete frame.",
    );
  }
}

async function connectBroker(pipePath, signal) {
  const socket = createConnection(pipePath);
  const abort = () => {
    socket.destroy(new Error("Desktop model request aborted."));
  };
  signal?.addEventListener("abort", abort, { once: true });
  try {
    await new Promise((resolve, reject) => {
      const connected = () => {
        socket.off("error", failed);
        resolve();
      };
      const failed = (error) => {
        socket.off("connect", connected);
        reject(error);
      };
      socket.once("connect", connected);
      socket.once("error", failed);
    });
  } catch (error) {
    signal?.removeEventListener("abort", abort);
    socket.destroy();
    throw error;
  }
  return {
    socket,
    detachAbort: () =>
      signal?.removeEventListener("abort", abort),
  };
}

function createBrokerStream(pipePath) {
  return (model, context, options) => {
    const stream = createAssistantMessageEventStream();
    const output = createOutput(model);

    (async () => {
      let socket;
      let detachAbort = () => {};
      try {
        const connection = await connectBroker(
          pipePath,
          options?.signal,
        );
        socket = connection.socket;
        detachAbort = connection.detachAbort;
        const requestId = randomUUID();
        const records = readRecords(socket);
        await writeRecord(socket, {
          type: "broker_hello",
          protocol: brokerProtocol,
        });
        const ready = await records.next();
        if (
          ready.done ||
          ready.value?.type !== "broker_ready" ||
          ready.value?.protocol !== brokerProtocol
        ) {
          throw new Error(
            "Desktop model broker failed protocol admission.",
          );
        }

        await writeRecord(socket, {
          type: "model_request",
          protocol: brokerProtocol,
          id: requestId,
          model: {
            provider: model.provider,
            id: model.id,
          },
          context,
          options: {
            maxTokens: options?.maxTokens,
            reasoning: options?.reasoning,
            sessionId: options?.sessionId,
          },
        });

        stream.push({ type: "start", partial: output });
        const textBlock = {
          type: "text",
          text: "",
        };
        output.content.push(textBlock);
        stream.push({
          type: "text_start",
          contentIndex: 0,
          partial: output,
        });
        const toolCalls = new Map();

        for await (const record of records) {
          if (record?.id !== requestId) {
            throw new Error(
              "Desktop model broker response id did not match.",
            );
          }
          if (record.type === "model_delta") {
            if (typeof record.delta !== "string") {
              throw new Error(
                "Desktop model broker emitted an invalid text delta.",
              );
            }
            textBlock.text += record.delta;
            stream.push({
              type: "text_delta",
              contentIndex: 0,
              delta: record.delta,
              partial: output,
            });
            continue;
          }
          if (record.type === "model_tool_call_start") {
            if (
              typeof record.toolCallId !== "string" ||
              typeof record.name !== "string" ||
              toolCalls.has(record.toolCallId)
            ) {
              throw new Error(
                "Desktop model broker emitted an invalid tool start.",
              );
            }
            const toolCall = {
              type: "toolCall",
              id: record.toolCallId,
              name: record.name,
              arguments: {},
              partialJson: "",
            };
            const contentIndex = output.content.length;
            output.content.push(toolCall);
            toolCalls.set(record.toolCallId, {
              contentIndex,
              toolCall,
            });
            stream.push({
              type: "toolcall_start",
              contentIndex,
              partial: output,
            });
            continue;
          }
          if (record.type === "model_tool_call_delta") {
            const state = toolCalls.get(record.toolCallId);
            if (!state || typeof record.delta !== "string") {
              throw new Error(
                "Desktop model broker emitted an invalid tool delta.",
              );
            }
            state.toolCall.partialJson += record.delta;
            try {
              state.toolCall.arguments = JSON.parse(
                state.toolCall.partialJson,
              );
            } catch {
              // Partial JSON is expected while a tool call is streaming.
            }
            stream.push({
              type: "toolcall_delta",
              contentIndex: state.contentIndex,
              delta: record.delta,
              partial: output,
            });
            continue;
          }
          if (record.type === "model_tool_call_end") {
            const state = toolCalls.get(record.toolCallId);
            if (!state) {
              throw new Error(
                "Desktop model broker ended an unknown tool call.",
              );
            }
            state.toolCall.arguments = JSON.parse(
              state.toolCall.partialJson || "{}",
            );
            delete state.toolCall.partialJson;
            stream.push({
              type: "toolcall_end",
              contentIndex: state.contentIndex,
              toolCall: state.toolCall,
              partial: output,
            });
            toolCalls.delete(record.toolCallId);
            continue;
          }
          if (record.type === "model_error") {
            throw new Error(
              typeof record.message === "string"
                ? record.message
                : "Desktop model broker rejected the request.",
            );
          }
          if (record.type === "model_done") {
            if (toolCalls.size !== 0) {
              throw new Error(
                "Desktop model broker left a tool call incomplete.",
              );
            }
            stream.push({
              type: "text_end",
              contentIndex: 0,
              content: textBlock.text,
              partial: output,
            });
            output.stopReason = [
              "stop",
              "length",
              "toolUse",
            ].includes(record.reason)
              ? record.reason
              : "stop";
            applyUsage(output, record.usage);
            stream.push({
              type: "done",
              reason: output.stopReason,
              message: output,
            });
            stream.end();
            socket.end();
            return;
          }
          throw new Error(
            "Desktop model broker emitted an unsupported frame.",
          );
        }
        throw new Error(
          "Desktop model broker closed before completing the request.",
        );
      } catch (error) {
        output.stopReason = options?.signal?.aborted
          ? "aborted"
          : "error";
        output.errorMessage =
          error instanceof Error
            ? error.message
            : String(error);
        stream.push({
          type: "error",
          reason: output.stopReason,
          error: output,
        });
        stream.end();
        socket?.destroy();
      } finally {
        detachAbort();
      }
    })();

    return stream;
  };
}

export function validateDesktopBrokerPipe(pipePath) {
  return (
    typeof pipePath === "string" &&
    pipePattern.test(pipePath)
  );
}

export function registerDesktopBrokerProvider(
  modelRuntime,
  pipePath,
) {
  if (!validateDesktopBrokerPipe(pipePath)) {
    throw new Error(
      "Desktop model broker pipe failed local-path admission.",
    );
  }
  modelRuntime.registerProvider(brokerProviderId, {
    name: "JARVIS Desktop Model Broker",
    api: "jarvis-desktop-broker",
    apiKey: "desktop-broker-capability",
    streamSimple: createBrokerStream(pipePath),
    models: [{
      id: brokerModelId,
      name: "JARVIS Desktop Default",
      api: "jarvis-desktop-broker",
      baseUrl: "jarvis-desktop-pipe",
      reasoning: false,
      input: ["text"],
      cost: {
        input: 0,
        output: 0,
        cacheRead: 0,
        cacheWrite: 0,
      },
      contextWindow: 128_000,
      maxTokens: 16_384,
    }],
  });
  const model = modelRuntime.getModel(
    brokerProviderId,
    brokerModelId,
  );
  if (!model || !modelRuntime.hasConfiguredAuth(brokerProviderId)) {
    throw new Error(
      "Desktop model broker provider failed model admission.",
    );
  }
  return model;
}

export {
  brokerModelId,
  brokerProtocol,
  brokerProviderId,
  maximumBrokerFrameBytes,
};
