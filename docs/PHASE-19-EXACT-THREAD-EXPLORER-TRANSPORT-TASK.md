# Phase 19 — exact-thread Explorer transport

Status: **TRANSPORT CORE AND UNLINKED WIN32 ADAPTER IMPLEMENTED — NO LIVE CONNECTION**

## Outcome

Phase 19 implements the host-side lifecycle for the thread-specific transport
selected by ADR-0001. It consumes one already admitted Explorer PID, Shell UI
thread ID and Shell HWND. It does not discover a process or window.

The component has two deliberately separate parts:

1. a portable, fault-injectable transport state machine coupled directly to
   the Phase 18 bridge core;
2. a real Win32 adapter that public CI compiles only to an unlinked `.obj`.

There is no executable controller, loader, command line, exported Hook
procedure or `DllMain`. The adapter is never linked or run by the test.

## Official Windows constraints

Microsoft documents that a nonzero `dwThreadId` binds `SetWindowsHookEx` to a
specific thread, while zero makes a desktop-wide hook. `WH_CALLWNDPROC` supports
thread scope. A cross-process Hook procedure must reside in a DLL, and the
procedure should chain with `CallNextHookEx` so other hooks keep working.

Microsoft also documents that a callback can still be executing after
`UnhookWindowsHookEx` returns. Phase 19 therefore treats successful unhook as
removal of the system entry, not proof that the module can be unloaded.

References:

- <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowshookexw>
- <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-unhookwindowshookex>
- <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid>
- <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-callnexthookex>

## Exact admission boundary

The fixed 80-byte request requires:

- one nonzero Explorer PID and one nonzero Shell thread ID;
- one nonzero Shell HWND whose observed PID and TID both match;
- nonzero module and Hook-procedure addresses supplied by a future collector;
- the same session nonce already prepared in the Phase 18 bridge;
- fresh host-admission, armed-kill-switch and one-shot-permit assertions;
- exact-thread scope and matching architecture;
- zeroed reserved fields.

The transport has no process-name fallback. It contains no `GetShellWindow`,
`FindWindow`, `EnumWindows`, process enumeration, remote memory, service,
registry, restart or termination path.

## Installation and drain order

The only real adapter installation call is structurally:

```text
validate HWND -> exact PID + exact nonzero TID
SetWindowsHookExW(WH_CALLWNDPROC, procedure, module, exactTid)
publish the external entry into the Phase 18 bridge
```

If bridge publication loses a race or fails, the transport closes the bridge
and removes the Hook. Quiesce follows this order:

```text
request cancellation
publish bridge pass-through
mark transport draining
wait for an in-flight installation call to return
unhook exactly once
wait for bridge callback ownership to reach zero
quiesced, but permanently pinned after any published entry
```

An unhook failure becomes `Faulted`; it is not silently retried and never
claims unload safety. A successful unhook still leaves `modulePinRequired=true`
after any external entry has existed.

## Deterministic evidence

`scripts/Test-ExplorerExactThreadTransport.ps1` performs eleven source and
contract checks. On the GitHub Windows runner it additionally:

- builds and runs the portable C++20 fault harness with `/W4 /WX`;
- verifies malformed identity and admission, platform failures, duplicate
  calls, callback ownership, unhook failure and truthful live-state reporting;
- blocks a synthetic install inside one thread while quiesce starts in another,
  then proves the resulting Hook is removed once and remains pinned;
- compiles the real Win32 adapter to an object file only;
- inspects that object for the three reviewed User32 symbols and rejects broad
  discovery, loader, remote-process, service and registry symbols;
- records the temporary object size and SHA-256, then deletes all outputs.

The harness uses injected fake platform calls. Its receipt always reports
`windowsAdapterExecuted=false`, `liveExplorer=not-run`,
`activationPermitted=false` and `mutationPerformed=false`.

## Boundary that remains closed

Phase 20 now provides the required DLL callback as a disk-only, empty-body
module and makes the bridge state optionally cross-process in that build. It
does not provide a collector that resolves module/procedure addresses or calls
the adapter.

The next development gate is a separate exact-target collector admission and
session-bound module package. Any first live use will still require a fresh
compatibility report, armed kill switch, exact source and binary hashes, one
active module, visible recovery terminal and explicit approval of the exact
one-shot command.
