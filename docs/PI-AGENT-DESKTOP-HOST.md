# Pi Agent desktop host

JarvisV2 will embed the official
`@earendil-works/pi-coding-agent` runtime behind a separate Node.js sidecar.
The desktop host is a .NET/WPF process, so the first cross-language boundary
uses strict LF-delimited JSONL over standard input and output.

The Pi Agent and Pi AI dependencies are pinned exactly to `0.82.1`. Pi exposes
both an SDK for
in-process JavaScript applications and RPC for language-neutral clients. The
sidecar uses the SDK package boundary while keeping Node and model-provider
state outside the Windows Shell process.

## Current implemented slice

`Jarvis.PiAgentHost` now:

- imports and fingerprints the pinned Pi package;
- verifies the session, runtime and model-control exports needed by the future
  desktop host;
- serves a per-frame-bounded JSONL handshake and capability protocol;
- provides a managed desktop bridge that starts the exact Node sidecar without
  a shell, replaces the inherited environment with a minimal OS allowlist,
  admits the ready frame, probes capabilities and owns orderly shutdown;
- creates one real Pi SDK session bound to one explicit canonical workspace
  root;
- keeps the session in memory and disables project resource discovery,
  sidecar provider network access and credential storage;
- registers one custom Pi provider only when the desktop supplies a reviewed
  `\\.\pipe\jarvis2-pi-model-{guid}` endpoint;
- keeps provider credentials out of the sidecar and sends model context through
  a bounded, current-user named pipe owned by the desktop process;
- provides a provider-neutral `DesktopModelBrokerServer` that remains alive
  across multiple requests, admits at most four current-user connections and
  caps every broker frame at 1 MiB;
- validates provider event order and permits only the session's `read`, `grep`,
  `find` and `ls` tool identities before forwarding tool calls to Pi;
- rejects an offline provider attempt to emit `bash`, returns a closed failure
  to the Pi turn and proves the broker remains isolated from the Shell;
- exposes conditional `start_turn` and `abort_turn` requests; assistant text,
  tool lifecycle and the terminal turn receipt stream independently of command
  responses;
- runs one managed output pump that demultiplexes concurrent responses and turn
  events, allowing the desktop to cancel generation while a prompt is active;
- replaces the SDK file tools with root-confined `read`, `grep`, `find` and
  `ls` definitions; `bash`, `edit` and `write` stay unavailable;
- rejects drive roots, protected OS/profile roots, relative paths, canonical
  aliases, junctions, symbolic links, workspace escapes and a second binding;
- rejects credential-shaped fields and frames over 64 KiB while accepting
  batched valid frames;
- reports whether any credential-shaped environment variable survived into the
  sidecar; the managed desktop bridge rejects readiness unless the result is
  clean;
- fault-injects a wrong ready protocol, an oversized ready frame and a hung
  startup; every case is rejected and cleanup is scoped to the owned Node
  process;
- fault-injects an invalid model pipe, wrong broker protocol, early disconnect
  and oversized broker response; each prompt fails closed;
- holds a model request open and proves both the Node host and managed desktop
  can abort the active Pi turn and observe `turn-aborted`;
- uses a deterministic `IDesktopModelProvider` to prove two ordinary turns,
  one real root-confined `read` tool round trip and active-turn cancellation
  through the production broker boundary without online model access;
- executes direct inside/outside file-tool probes and forces `PI_OFFLINE=1`
  before importing Pi.

This is now a real desktop-owned Pi conversation transport, but it is not yet a
product chat surface. With no broker pipe, readiness and capabilities continue
to report `promptingEnabled: false`. With the reviewed pipe present, the bridge
can bind one read-only workspace, run a real Pi prompt and receive incremental
assistant text across multiple turns. A turn runs in the background, so the
desktop can issue `abort_turn` without waiting for generation to finish. No
provider credential is inherited or transported, no resource is discovered
from the workspace and no session file is created.

The broker server and provider interface are production-facing boundaries; the
provider used by the audit is still deterministic and offline. Connecting an
authenticated production model provider, choosing its credential store and
building the product conversation surface remain separate reviewed steps.

## Prompting admission

Pi runs with the permissions of its host process and does not provide a
built-in operating-system permission sandbox. Jarvis therefore cannot treat
an authenticated agent session as equivalent to a UI widget. Workspace
admission remains independent from provider authentication. Prompting is
enabled only by a desktop-owned named-pipe capability; mutation tools and
unattended self-iteration still require separate supervisor capabilities.

The planned progression is:

```text
WPF desktop
    |
    +-- managed sidecar lifecycle and bounded JSONL transport
            |
            +-- single-root read-only Pi session admission (implemented)
                    |
                    +-- brokered streaming + active-turn abort (implemented)
                            |
                            +-- provider-neutral multi-request broker
                                (implemented)
                                    |
                                    +-- authenticated production provider
                                            |
                                            +-- product conversation surface
                                                    |
                                                    +-- per-session mutation capability
                                                            |
                                                            +-- reviewed self-iteration workflow
```

No stage grants Shell injection, Explorer mutation, registry writes or
physical-device control.

## Validation

Install the locked dependency without lifecycle scripts, then run the full
audit:

```powershell
pnpm install --frozen-lockfile --ignore-scripts

pwsh -NoLogo -NoProfile -File `
  .\scripts\Test-PiAgentHost.ps1 `
  -NodePath C:\path\to\node.exe `
  -DotnetPath C:\path\to\dotnet.exe
```

CI uses `-StaticOnly` to build the managed bridge and validate the exact package
lock, schema and source boundary without provider credentials. The full local
audit additionally creates an offline, in-memory SDK session, proves
single-root binding, executes inside/outside path and junction rejection tests,
and completes three turns through the desktop-owned broker. The third turn
executes the real root-confined `read` tool and requires a second model request.
A separate held request proves cancellation through the concurrent desktop
response pump. The valid path observes five model requests and zero broker
faults; an isolated negative provider records exactly one rejected `bash`
fault. No path contacts an online model or transports a provider credential.
