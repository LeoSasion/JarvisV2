# Pi Agent desktop host

JarvisV2 will embed the official
`@earendil-works/pi-coding-agent` runtime behind a separate Node.js sidecar.
The desktop host is a .NET/WPF process, so the first cross-language boundary
uses strict LF-delimited JSONL over standard input and output.

The dependency is pinned exactly to `0.82.1`. Pi exposes both an SDK for
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
  provider network access, credential storage and prompting;
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
- executes direct inside/outside file-tool probes and forces `PI_OFFLINE=1`
  before importing Pi.

This is now a real desktop-owned Pi session admission path, but it is not yet a
chat surface. The bridge can launch and supervise the isolated sidecar and bind
one read-only workspace. No prompt request exists, no provider credential is
inherited or transported, no resource is discovered from the workspace and no
session file is created.

## Why prompting remains disabled

Pi runs with the permissions of its host process and does not provide a
built-in operating-system permission sandbox. Jarvis therefore cannot treat
an authenticated agent session as equivalent to a UI widget. Workspace
admission is now independent from provider authentication and conversation.
Prompting, mutation tools and unattended self-iteration must each become
explicit supervisor capabilities.

The planned progression is:

```text
WPF desktop
    |
    +-- managed sidecar lifecycle and bounded JSONL transport
            |
            +-- single-root read-only Pi session admission (implemented)
                    |
                    +-- authenticated desktop conversation gate
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
single-root binding and executes inside/outside path and junction rejection
tests; it does not send a model prompt.
