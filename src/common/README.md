# Common Windows source

This directory contains projects intended to be shared by Windows 10 and
Windows 11 after platform-specific admission.

Do not move code here merely because it compiles on both systems. A component
belongs here only when it has no private Win11 symbol, Win11-only selector or
Win11-only DWM assumption. Existing assembly names and namespaces remain
stable.

`Jarvis.VisualEffects` is a platform-neutral `net8.0` library for the visual
state shared by Win10 and Win11 renderers. It owns continuous RGB sampling,
the `jarvis-visual-signal-v1` linear-sRGB semantic frame, the typed Neural Void
particle/post contract and schema-versioned inert preset validation. It has no
WPF, native Windows, Shell or device SDK dependency. Unknown preset versions,
enabled effects, malformed channels and device-I/O requests fail closed to an
inactive state. Its retained vector-scene contract stores mathematical point,
line, polyline, arc and plane commands in a design coordinate space, separates
static from per-frame updates and rejects literal colors, bitmaps, runtime
effects, unstable ordering and quality-budget overflow.

`Jarvis.PiAgentHost` is the language-neutral AI runtime boundary shared by
both Windows backends. It pins the official Pi SDK, resolves only its reviewed
core modules through a fail-closed adapter, verifies a bounded JSONL sidecar
protocol and includes the managed desktop-owned sidecar lifecycle. It
creates one root-confined in-memory session, replaces the child environment
with a minimal OS allowlist and enables prompting only through a
desktop-owned, current-user named pipe. Its asynchronous turn transport can
stream events and cancel active generation. It contains no Shell or platform
styling transport. `PiAgentDesktopRuntime` is the composition root that owns
the broker, sidecar, admitted session and conversation state, then quiesces and
disposes them in reviewed order. A caller-supplied
`PiAgentConversationCheckpointStore` binds bounded completed-text history to
one workspace, protects it with Windows CurrentUser DPAPI, commits it
atomically after each terminal turn, closes submissions on persistence failure
and restores it before a new sidecar session.
