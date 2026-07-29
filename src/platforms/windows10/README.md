# Windows 10 backend

The first Win10 vertical slice is implemented in
`Jarvis.Win10.NativeStyleProbe`. It reads exact host and DWM state, verifies
the reviewed dark-caption attribute on an owned HWND, and provides an
interactive own-process visual surface.

The second slice, `Jarvis.Win10.ShellSurfaceProbe`, records bounded,
text-free class topology for the desktop, File Explorer and classic taskbar.
Both tools consume `Jarvis.Win10.HostAdmission`, which owns the exact
`win10-22h2-19045.6466-x64` identity gate.

See `WINDOWS10-HANDOFF.md`,
`docs/WINDOWS10-NATIVE-STYLE-PROBE.md` and
`docs/WINDOWS10-SHELL-SURFACE-INVENTORY.md` for commands and receipts.

The Shell probe reads window structure but does not collect titles or modify
Explorer. Neither probe uses Windhawk.
Future projects continue to use `Jarvis.Win10.<Feature>` and must not reuse
Win11 private symbols, selectors, DWM backdrops or module IDs.
