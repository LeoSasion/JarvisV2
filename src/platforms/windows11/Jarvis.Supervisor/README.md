# JARVIS2 Supervisor

This is the first recovery boundary for the native-shell experiment. It has no
GUI, does not inject code, and never reads or changes Windhawk configuration.

Commands:

- `inspect` is read-only. It fingerprints Windows and the verified desktop
  shell, then binds the loaded `Taskbar.View.dll`, `SystemTray.dll`, and
  `SearchUx.UI.dll` paths to that one shell PID.
- `arm-kill-switch` atomically creates
  `%LOCALAPPDATA%\JARVIS2\disabled.flag`. First-party JARVIS2 mods must check
  this file before registering any hook. After the flag is confirmed, the
  command revokes `active-module.txt`.
- `clear-kill-switch --module <id> --confirm` accepts only an allowlisted,
  case-sensitive module id. While holding `Local\JARVIS2.StateGate.v1`, it
  writes the exact ASCII id to `active-module.txt`, confirms the emergency flag
  is still armed, and only then deletes the flag. The permit expires after five
  minutes and its expiry is included in command/inspect output. There is no
  force option. At this milestone only `jarvis-taskbar-icon-size` is eligible;
  the larger M1 visual module remains build-only pending runtime revocation.
- `restart-explorer --confirm` identifies the real desktop shell only when
  `GetShellWindow` and `Shell_TrayWnd` have the same verified PID. It holds the
  state gate and a no-delete handle to `disabled.flag` for the whole bounded
  recovery and never terminates unrelated `explorer.exe` processes.

The Supervisor safety path is fixed to the Windows LocalApplicationData known
folder. Native modules must resolve the same known folder and consume the
permit while holding `Local\JARVIS2.StateGate.v1` before registering hooks.
An Explorer-hosted module must acquire that gate non-blockingly (or with only a
very short timeout) and fail closed when it is busy; recovery intentionally
holds the gate while waiting for the replacement shell and must not deadlock on
a module initializer.

Exit codes are stable for scripts: `0` success, `2` invalid usage, `3`
unsupported platform, `10` incompatible host, `11` blocked by a safety
interlock, and `20` operation failure.
