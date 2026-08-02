# Phase 20 — disk-only CallWndProc bridge

Status: **EMPTY CALLBACK DLL BUILT — NEVER LOADED OR CONNECTED**

## Outcome

Phase 20 produces the first complete DLL shape for the thread-specific host:

- the four Phase 18 bridge exports;
- one `JarvisBridge_CallWndProc` export with the Windows `HOOKPROC` signature;
- one optional shared bridge instance used only in this DLL build;
- a zero PE entry point with no CRT or custom DLL startup;
- no callback body, selector, property access or visual mutation.

Public CI builds and inspects the AMD64 DLL as a file. It never calls
`LoadLibrary`, invokes an export, installs a Hook or connects to Explorer.

## Why the bridge instance must be shared

Normal DLL writable data is private to each process. Initializing the bridge
in a future collector process would otherwise leave the copy mapped into
Explorer in `Cold`, making callback ownership and quiesce ineffective.

MSVC documents that a section declared with the `shared` attribute is shared
among processes that load the image. The Phase 20 build therefore places only
`jarvis_bridge_core_instance` in `.jvbrdg`:

- read/write/shared;
- non-executable;
- `constinit` to avoid a per-process dynamic initializer;
- fixed storage with no pointers, handles, allocator state or locks;
- 32-bit atomics required to be always lock-free.

The ordinary Phase 18 bridge build does not define the shared-instance macro
and remains process-private.

The shared section is a lifecycle mechanism, not an authorization primitive.
A future activation package must bind a session nonce and exact identity, use
a private non-user-writable module path and verify its DACL and file hash. One
active module per Explorer lifetime remains mandatory.

Reference:

- <https://learn.microsoft.com/cpp/preprocessor/section?view=msvc-170>
- <https://learn.microsoft.com/cpp/cpp/allocate?view=msvc-170>

## Callback contract

Microsoft's `CallWndProc` contract requires a negative `nCode` to go directly
to `CallNextHookEx`; for nonnegative codes, chaining and returning the next
Hook result is strongly recommended.

`JarvisBridge_CallWndProc` therefore follows this fixed envelope:

```text
nCode < 0
  -> CallNextHookEx unchanged

nCode >= 0
  -> read GetCurrentProcessId / GetCurrentThreadId
  -> try Phase 18 callback ownership using that exact identity
  -> if rejected: CallNextHookEx unchanged
  -> if admitted: empty body
  -> leave Phase 18 callback ownership
  -> CallNextHookEx unchanged
```

It does not dereference the `CWPSTRUCT`, examine a message, allocate, wait,
lock, log or catch an exception. The body argument used by the portable test
core is `nullptr` in the real Windows export.

References:

- <https://learn.microsoft.com/windows/win32/winmsg/callwndproc>
- <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-callnexthookex>
- <https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getcurrentprocessid>
- <https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getcurrentthreadid>

## Deterministic evidence

`scripts/Test-ExplorerCallWndProcBridge.ps1` performs eleven source checks. On
the GitHub Windows runner it additionally:

- builds and runs the portable callback/bridge harness with `/W4 /WX`;
- proves negative codes, missing chain, pass-through, PID/TID mismatch,
  admitted enter/body/leave, result preservation and post-quiesce rejection;
- holds one admitted callback while quiesce begins and proves the callback
  leaves before the next Hook is called;
- races 4,000 callback dispatches with quiesce and requires every notification
  to chain exactly once with no ownership leak;
- builds the shared callback DLL but never loads it;
- verifies exactly five exports;
- verifies the PE entry point is zero and no CRT/custom startup exists;
- verifies `.jvbrdg` is shared, readable, writable and non-executable;
- verifies the callback imports `CallNextHookEx`, `GetCurrentProcessId` and
  `GetCurrentThreadId`, while loader, installer, discovery and mutation APIs
  remain absent;
- records the temporary DLL SHA-256 and size, then deletes it.

Every receipt keeps `callbackDllExecuted=false`, `liveExplorer=not-run`,
`activationPermitted=false` and `mutationPerformed=false`.

## Boundary that remains closed

No collector currently:

- resolves or loads this DLL;
- obtains its module handle or callback address;
- calls the Phase 19 Win32 adapter;
- triggers an initial target-thread message;
- applies, restores or even reads a visual property;
- clears the kill switch or creates a permit.

The next phase must model a separate collector that accepts one already
verified HWND/PID/TID and one reviewed DLL path. Its admission must bind the
module SHA-256, private path/DACL, callback export, architecture, session nonce,
recovery lease and exact one-shot permit before producing a non-executing
session package. Live use remains a separate explicit approval gate.
