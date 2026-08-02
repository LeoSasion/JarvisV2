# ADR-0001: Explorer-only host boundary

- Status: accepted for offline modelling only
- Date: 2026-07-27
- Live implementation: prohibited
- Supersedes: Windhawk service as a JarvisV2 activation host

## Context

The controlled disabled-host rehearsal started the Windhawk 1.7.3 service
while M2 remained disabled. The Windhawk base runtime nevertheless appeared in
Explorer and unrelated processes. M2 itself was never enabled or mapped.
Recovery normally stopped the service without restarting or terminating
Explorer.

This is expected from Windhawk's architecture, but it is incompatible with the
JarvisV2 no-global-injection boundary:

1. the service enables debug privilege, creates `EngineControl`, and asks it to
   handle processes;
2. `EngineControl` loads `windhawk.dll` and starts a global hook session;
3. that session constructs `AllProcessesInjector`;
4. the injector uses `NtGetNextProcess` to enumerate accessible processes;
5. its loader allocates and writes remote memory and uses a remote thread or
   APC path to load the base runtime.

The relevant upstream snapshot is Windhawk commit
`61fc60dad607e6888d8de560d1b6add716f936c3`:

- [service starts the engine](https://github.com/ramensoftware/windhawk/blob/61fc60dad607e6888d8de560d1b6add716f936c3/src/windhawk/app/service.cpp#L211-L250)
- [engine loads `windhawk.dll`](https://github.com/ramensoftware/windhawk/blob/61fc60dad607e6888d8de560d1b6add716f936c3/src/windhawk/app/engine_control.cpp#L7-L39)
- [global session constructs the all-process injector](https://github.com/ramensoftware/windhawk/blob/61fc60dad607e6888d8de560d1b6add716f936c3/src/windhawk/engine/main.cpp#L82-L115)
- [injector enumerates processes](https://github.com/ramensoftware/windhawk/blob/61fc60dad607e6888d8de560d1b6add716f936c3/src/windhawk/engine/all_processes_injector.cpp#L423-L488)
- [loader writes remote memory and runs it](https://github.com/ramensoftware/windhawk/blob/61fc60dad607e6888d8de560d1b6add716f936c3/src/windhawk/engine/dll_inject.cpp#L674-L823)

Therefore, disabling one mod controls whether that mod is loaded by the
already-injected engine. It does not turn the engine into an Explorer-only
host.

## Windows mechanism review

There is no public Windows API for replacing the Windows 11 taskbar's private
icon-size calculation. Any true in-process modification remains an
unsupported experiment.

The considered transport mechanisms are:

| Mechanism | Decision | Reason |
| --- | --- | --- |
| Kernel driver | rejected | Violates the no-driver and stability boundary. |
| `AppInit_DLLs` | rejected | Global, legacy, registry-backed and incompatible with the project boundary. |
| Global `SetWindowsHookEx` | rejected | A zero thread ID affects the caller's desktop and repeats the exact broad-scope mistake. |
| Remote memory plus remote thread/APC | rejected for Phase 6 | Broad rights, more lifetime states, and easy scope expansion; Windhawk already demonstrates why this transport must not be inherited wholesale. |
| Thread-specific `SetWindowsHookEx` | review candidate only | Microsoft documents a nonzero thread ID as thread-scoped, so it can express one exact Shell-window thread without process enumeration. It still injects a DLL and is not approved for live use. |

Microsoft documents that `GetShellWindow` returns the Shell desktop window and
`GetWindowThreadProcessId` returns that window's creator PID and TID. These
form a stronger identity root than searching for every process named
`explorer.exe`. A future collector would then have to verify the exact image
path, session, product version, image hash, architecture, start time and
module identity before producing a candidate.

`SetWindowsHookEx` is not treated as a complete lifecycle solution. Microsoft
also states that a hook can still be executing after `UnhookWindowsHookEx`
returns. The module must therefore own a separate, proven quiesce and lifetime
protocol. Complex initialization must occur in an explicit exported function,
not `DllMain`, because `DllMain` executes under the loader lock.

## Decision

Phase 6 implements only a portable offline admission model:

- input is explicitly labelled `offline-fixture`;
- it never enumerates or opens a process;
- it contains no P/Invoke, service, registry, remote-memory or hook-install API;
- it rejects a zero TID, more than one Shell candidate, session drift, image or
  module drift, a running Windhawk service, existing Windhawk/Jarvis mappings,
  a disarmed kill switch, or a present one-shot permit;
- it rejects the current `windhawk-mod-v1` module contract;
- it accepts only a future `standalone-explicit-init-v1` bridge;
- even a passing fixture produces a review candidate with
  `executionSupported=false`, `activationPermitted=false`,
  `liveExplorer=not-run`, and `mutationPerformed=false`.

The current M2 binary cannot be used by the replacement host unchanged. Its
settings, logging and symbol-hook lifecycle are Windhawk APIs. A future bridge
must expose a small standalone ABI and either port the required GPL code with
attribution or replace it with independently reviewed components.

## Required future ABI

No ABI is implemented in this phase. The review target is:

1. `JarvisBridge_QueryContract` returns a fixed contract and build identity
   without side effects.
2. `JarvisBridge_Initialize` receives already-validated immutable settings and
   returns only after hook ownership is published.
3. `JarvisBridge_Quiesce` atomically restores pass-through before any unload
   decision.
4. `JarvisBridge_QueryState` returns fixed, allocation-free state.
5. physical unload is optional and forbidden when callback ownership is not
   proven quiescent.

Implementation note (2026-08-03): Phase 18 now implements this standalone ABI
and its callback ownership core. Phase 19 adds the separately reviewable
exact-thread transport lifecycle plus a Win32 adapter that CI compiles only to
an unlinked object. Neither phase adds a collector, loader, exported Hook
procedure or live connection, so the decision below remains unchanged.

## Live gate remains closed

This ADR does not authorize a collector, launcher, Hook, injection, module
conversion or Explorer experiment. Before any live proposal:

1. the standalone bridge must exist and pass its native fault lab;
2. the collector and transport must be separate binaries;
3. the transport must accept one already-verified PID and nonzero TID only;
4. source scans and executable tests must prove no global scope, process-name
   enumeration, `dwm.exe`, service, driver, Explorer restart or force-stop
   path;
5. the host must remain locked by default with a fresh recovery design;
6. a new exact user approval must name the exact binaries, hashes, PID, TID and
   one-shot command.

## Primary references

- [Windhawk source and GPL-3.0 license](https://github.com/ramensoftware/windhawk)
- [Windhawk author's global-injection design](https://m417z.com/Implementing-Global-Injection-and-Hooking-in-Windows/)
- [Microsoft `SetWindowsHookEx`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw)
- [Microsoft `UnhookWindowsHookEx`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-unhookwindowshookex)
- [Microsoft `GetShellWindow`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getshellwindow)
- [Microsoft `GetWindowThreadProcessId`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid)
- [Microsoft `QueryFullProcessImageNameW`](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew)
- [Microsoft `ProcessIdToSessionId`](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-processidtosessionid)
- [Microsoft `WinVerifyTrust`](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/nf-wintrust-winverifytrust)
- [Microsoft `DllMain` restrictions](https://learn.microsoft.com/en-us/windows/win32/dlls/dllmain)
