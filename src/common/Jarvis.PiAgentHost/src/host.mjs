import { loadContract } from "./contract.mjs";
import { inspectPiRuntime } from "./pi-runtime-inspector.mjs";
import { createReadyEvent, handleRequest } from "./protocol.mjs";

function writeRecord(record) {
  process.stdout.write(`${JSON.stringify(record)}\n`);
}

async function serve(contract, runtimeReceipt) {
  writeRecord(createReadyEvent(contract, runtimeReceipt));
  process.stdin.setEncoding("utf8");
  let buffer = "";
  let shuttingDown = false;

  for await (const chunk of process.stdin) {
    buffer += chunk;
    let newlineIndex = buffer.indexOf("\n");
    while (newlineIndex >= 0) {
      const line = buffer.slice(0, newlineIndex);
      buffer = buffer.slice(newlineIndex + 1);
      newlineIndex = buffer.indexOf("\n");
      if (line.trim().length === 0) {
        continue;
      }
      if (
        Buffer.byteLength(line, "utf8") >
        contract.transport.maxFrameBytes
      ) {
        writeRecord({
          type: "fatal",
          error: {
            code: "frame-too-large",
            message: "A JSONL frame exceeded the contract limit.",
          },
        });
        process.exitCode = 13;
        return;
      }

      let request;
      try {
        request = JSON.parse(line);
      } catch {
        writeRecord({
          type: "response",
          id: null,
          command: "invalid",
          success: false,
          error: {
            code: "invalid-json",
            message: "The request is not valid JSON.",
          },
        });
        continue;
      }

      const result = handleRequest(
        request,
        contract,
        runtimeReceipt,
      );
      writeRecord(result.response);
      if (result.shutdown) {
        shuttingDown = true;
        break;
      }
    }
    if (shuttingDown) {
      return;
    }
    if (
      Buffer.byteLength(buffer, "utf8") >
      contract.transport.maxFrameBytes
    ) {
      writeRecord({
        type: "fatal",
        error: {
          code: "frame-too-large",
          message: "The pending JSONL frame exceeded the contract limit.",
        },
      });
      process.exitCode = 13;
      return;
    }
  }
}

async function main() {
  const command = process.argv[2];
  const contract = await loadContract();
  const runtimeReceipt = await inspectPiRuntime(contract);
  if (runtimeReceipt.result !== "passed-embedded-dependency") {
    writeRecord(runtimeReceipt);
    process.exitCode = 12;
    return;
  }

  if (command === "inspect") {
    writeRecord(runtimeReceipt);
    return;
  }
  if (command === "serve") {
    await serve(contract, runtimeReceipt);
    return;
  }

  process.stderr.write(
    "Usage: node ./src/host.mjs <inspect|serve>\n",
  );
  process.exitCode = 2;
}

await main();
