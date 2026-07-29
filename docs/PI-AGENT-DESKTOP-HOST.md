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
- exposes only `read`, `grep`, `find` and `ls` as the intended first tool set;
- rejects `start_session` with `policy-disabled`;
- rejects credential-shaped fields and frames over 64 KiB while accepting
  batched valid frames;
- forces `PI_OFFLINE=1` before importing Pi.

This is a real dependency and transport probe, not yet a chat session. The
desktop does not launch it yet, no provider credentials are read, and no Pi
session is created.

## Why the boundary starts disabled

Pi runs with the permissions of its host process and does not provide a
built-in operating-system permission sandbox. Jarvis therefore cannot treat
an authenticated agent session as equivalent to a UI widget. Session
creation, workspace binding, mutation tools and unattended self-iteration
must each become explicit supervisor capabilities.

The planned progression is:

```text
WPF desktop
    |
    +-- bounded JSONL sidecar transport (implemented)
            |
            +-- read-only Pi session admission
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
  -NodePath C:\path\to\node.exe
```

CI uses `-StaticOnly` to validate the exact package lock, schema, source
boundary and disabled capabilities without provider credentials or a live
agent session.
