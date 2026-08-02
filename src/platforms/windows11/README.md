# Windows 11 backend

This directory preserves the existing Win11 25H2 native-shell implementation,
offline Explorer models and build-locked Supervisor.

File movement does not change activation status. Existing compatibility,
canonical build and live-authorization gates remain mandatory.

`Jarvis.ExplorerBridgeCore` is the first real standalone PE boundary for the
future exact Explorer host. It implements ABI v2 preparation, exact identity,
atomic callback ownership, pass-through-before-drain and conservative module
pinning. It deliberately contains no Hook installer, loader, process discovery
or visual mutation, so it cannot connect to Explorer by itself. See
`docs/PHASE-18-STANDALONE-EXPLORER-BRIDGE-CORE-TASK.md`.

`Jarvis.ExplorerExactThreadTransport` adds the separately reviewable host-side
state machine and Win32 adapter for one pre-admitted Shell HWND/PID/nonzero-TID
tuple. The adapter calls only `GetWindowThreadProcessId`, thread-scoped
`SetWindowsHookExW(WH_CALLWNDPROC, ..., shellThreadId)` and
`UnhookWindowsHookEx`. Public CI compiles it to an unlinked object and runs the
portable fault harness; there is still no loader, controller, exported Hook
procedure or live Explorer connection. See
`docs/PHASE-19-EXACT-THREAD-EXPLORER-TRANSPORT-TASK.md`.
