# Pi Agent OpenAI Responses provider

`OpenAiResponsesModelProvider` is the first opt-in production model boundary
for the native JARVIS desktop. It lives in the managed desktop process behind
`IDesktopModelProvider`; the Pi Node sidecar stays offline and talks only to a
current-user named pipe.

## Reviewed API contract

The provider sends desktop-authenticated HTTPS requests only to
`https://api.openai.com/v1/responses`. It selects `gpt-5.6-sol`, uses `medium`
reasoning, requests server-sent-event streaming, and sends `store: false`.
This follows the official guidance to use the Responses API for agentic,
multi-turn work and the current model guidance that identifies `gpt-5.6-sol`
as the coding-focused GPT-5.6 model:

- [latest model guide](https://developers.openai.com/api/docs/guides/latest-model);
- [GPT-5.6 SOL model](https://developers.openai.com/api/docs/models/gpt-5.6-sol);
- [streaming Responses](https://developers.openai.com/api/docs/guides/streaming-responses);
- [function calling](https://developers.openai.com/api/docs/guides/function-calling).

The SSE adapter recognizes text deltas, streamed function arguments, completed
function calls, usage and terminal response events. It fails closed on
malformed JSON, changed function arguments, unknown tools, non-SSE success
responses, oversized events or cumulative function arguments, HTTP errors and
streams without a terminal response.

## Tool boundary

Only these Pi tools may be described to or invoked by the provider:

- `read`;
- `grep`;
- `find`;
- `ls`;
- `propose_edit`, which can only stage a non-mutating existing-text proposal;
- `propose_create_file`, which can only stage one missing UTF-8 file beneath an
  existing parent and never creates or overwrites anything itself.

`bash`, `edit`, `write` and every unknown tool are rejected before a request is
sent. The provider maps Pi tool calls to Responses function tools and maps
their results back through `function_call_output` using the original call ID.
All file operations remain subject to Pi's single-workspace, reparse-point and
escape checks. The provider cannot call the desktop-only approve or reject
requests, so the model cannot exercise mutation authority.

## Credential lifecycle

The setup dialog accepts an API key only in a WPF `PasswordBox` after an
explicit user click. `OpenAiApiKeyCredentialStore` validates it and protects
it with Windows CurrentUser DPAPI plus path-bound entropy. The JSON envelope is
written through a temporary file with write-through semantics and atomically
moved to:

```text
%LOCALAPPDATA%\JARVIS2\credentials\openai-api-key.j2secret
```

The old value is never displayed. Ambient `OPENAI_API_KEY` values are not
read. The plaintext key is used only to construct the desktop HTTPS
`Authorization: Bearer` header; it is not written into the request body,
broker frames, sidecar environment, package, diagnostics or logs.

## Runtime selection

Local deterministic mode remains the default:

```powershell
jarvis-control-center.exe --conversation `
  --workspace C:\absolute\workspace `
  --provider local
```

After configuring a key from the idle Control Center, production mode is an
explicit relaunch:

```powershell
jarvis-control-center.exe --conversation `
  --workspace C:\absolute\workspace `
  --provider openai
```

## Verification boundary

`openai-provider-probe` injects a synthetic `HttpMessageHandler`; it performs
no live model request. It verifies the outbound JSON, header-only credential,
text and tool SSE streams, usage mapping, CurrentUser DPAPI round trip,
ciphertext-at-rest, corrupt-envelope rejection, error redaction, malformed
stream and oversized-argument rejection, and cancellation.
`scripts/Test-PiAgentHost.ps1` runs that probe in the current Windows user
context.

This implementation is production-capable but not production-authenticated in
the repository. No API key is committed or bundled, and no live OpenAI request
is claimed by the offline receipt.
