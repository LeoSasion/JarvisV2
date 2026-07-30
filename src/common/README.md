# Common Windows source

This directory contains projects intended to be shared by Windows 10 and
Windows 11 after platform-specific admission.

Do not move code here merely because it compiles on both systems. A component
belongs here only when it has no private Win11 symbol, Win11-only selector or
Win11-only DWM assumption. Existing assembly names and namespaces remain
stable.

`Jarvis.PiAgentHost` is the language-neutral AI runtime boundary shared by
both Windows backends. It pins the official Pi SDK, verifies a bounded JSONL
sidecar protocol and includes the managed desktop-owned sidecar lifecycle. It
creates one root-confined in-memory session, replaces the child environment
with a minimal OS allowlist and enables prompting only through a
desktop-owned, current-user named pipe. It contains no Shell or platform
styling transport.
