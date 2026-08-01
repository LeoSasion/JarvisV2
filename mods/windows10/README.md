# Windows 10 native modules

No Win10 module is shipped in this handoff.

Future modules require new `jarvis-win10-*` IDs, exact Win10 build fingerprints,
a fixed toolchain receipt and a transport that does not use the quarantined
Windhawk service host.

The managed `Jarvis.Win10.DesktopStyleSession` adapter is not a module. Its
bounded desktop `SysListView32` text-color preview uses the reviewed scalar
message session and does not load code into Explorer.

`Jarvis.Win10.ExplorerCaptionPlan` is also managed and non-modular. It reads
one exact Explorer window's DWM dark-caption value and emits a non-executable
rollback plan; it contains no DWM write API.

`Jarvis.Win10.ExplorerCaptionSession` is a separate managed, non-module
transaction. It can set only DWM attribute 20 on one exact Explorer HWND. Its
two approved runs passed API readback, and the second passed nonclient repaint
plus exact rollback, but neither changed a title-bar pixel. With the
application theme in light mode, the exact profile now disables another
caption write. No Win10 DLL exists.

`Jarvis.Win10.ExplorerCaptionOverlay` is also not a module. It creates only an
owned, short-lived WPF overlay window and tracks one read-only-admitted
Explorer rectangle. The window is click-through and non-activating; no DLL is
injected and no Explorer state is written.
