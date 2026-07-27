# Phase 8: Native Explorer window style session

Status: **IMPLEMENTED — LIVE APPLY REQUIRES SEPARATE EXACT APPROVAL**

## Why this replaces the desktop text-color direction

The Windows 11 desktop `SysListView32` accepted and returned
`LVM_SETTEXTCOLOR`, but the graphite, amber and restored `Progman` captures
were pixel-identical. That path changed legacy state without producing a
visible result and is not a viable styling primitive.

The next visible checkpoint targets the non-client frame of one real,
temporary File Explorer window. Windows 11 publicly supports
`DWMWA_BORDER_COLOR`, `DWMWA_CAPTION_COLOR` and `DWMWA_TEXT_COLOR` for this
surface.

## Baseline and rollback contract

The color attributes are settable but are not readable on the validated File
Explorer host (`DwmGetWindowAttribute` returned `E_INVALIDARG`). The controller
therefore does not claim to preserve unknown custom colors.

Live use is limited to a newly opened File Explorer window with system-default
colors. The caller must explicitly acknowledge that baseline. Reset writes the
documented `DWMWA_COLOR_DEFAULT` value (`0xFFFFFFFF`) to all three attributes.
The temporary window is closed after visual evidence is captured.

## Safety boundary

- [x] Require an exact hexadecimal HWND.
- [x] Require `CabinetWClass`.
- [x] Require the exact independent Explorer PID and full window title.
- [x] Revalidate the complete identity immediately before apply and reset.
- [x] Change only DWM border, caption and caption-text `COLORREF` attributes.
- [x] Call `DwmFlush` after apply and reset.
- [x] Persist the target, preset, TTL and baseline contract before apply.
- [x] Limit every preview to 10–60 seconds.
- [x] Route TTL, Ctrl+C and exceptions through default reset.
- [x] Never reset a replacement HWND.
- [x] Require separate exact apply and reset confirmations.
- [x] Do not change the client area, geometry, registry, service or process.
- [x] Do not inject, hook, start Windhawk, restart Explorer or terminate a
  process.

## Signal preset

- Border: cyan `#00E5FF`
- Caption: deep teal `#123840`
- Caption text: amber `#FFD166`

The deliberately high-contrast colors make a visual no-op impossible to
mistake for success.

## Acceptance before live approval

1. Release build is warning-free.
2. All offline policy scenarios pass.
3. Static audit proves the exact seven-entry native API allowlist.
4. A read-only plan binds the live temporary window HWND/PID/title.
5. Public CI and the project gate run only offline audit.
6. The user reviews and approves the exact apply and emergency reset commands.

## Acceptance after a live preview

1. The native Explorer title bar and border visibly change in a real capture.
2. Every DWM HRESULT is `S_OK`.
3. The controller resets all three attributes to system default.
4. Before/active/after captures make the visual transition explicit.
5. The temporary window closes, Explorer does not restart, Windhawk stays
   stopped/manual, and no JARVIS process remains.
