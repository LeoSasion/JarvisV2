# Windows 10 owned Explorer caption overlay

`Jarvis.Win10.ExplorerCaptionOverlay` is the bounded fallback after two live
tests proved that `DWMWA_USE_IMMERSIVE_DARK_MODE` read back correctly on this
Windows 10 host but did not change a captured Explorer title-bar pixel. The
failed writer capability remains absent from the exact host profile.

## Safety boundary

The overlay is a window owned by the JARVIS preview process. It does not alter
the Explorer window it visually covers. It:

- builds as a WPF `WinExe`, so no console window can take focus or obscure the
  target during a preview;
- reuses the exact read-only Explorer admission gate;
- binds to one expected `CabinetWClass` HWND/PID/TID identity;
- permits the exact gate's desktop-Shell PID mismatch only when it is the sole
  failure and that PID belongs to the probe's observed Explorer process set,
  supporting Win10's separate folder-process mode without relaxing any writer;
- reads only target validity, rectangle, DPI and foreground state;
- applies `WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW` and `WS_EX_NOACTIVATE` only
  to its own HWND;
- returns `HTTRANSPARENT` from its own hit-test procedure;
- ends 138 DPI-aware DIPs before Explorer's native caption controls, leaving
  the real minimize, maximize and close visuals and interactions unobscured;
- appears only while the admitted Explorer window is foreground;
- closes after a confirmed 10-60 second TTL or immediately when the target
  identity retires.

It does not call a DWM setter, send a message to Explorer, install a hook,
inject a DLL, start Windhawk, restart Explorer, change the registry or clear
the JARVIS2 kill switch. The overlay is a visual prototype, not proof that an
Explorer-hosted implementation is safe or compatible.

## Static and model verification

```powershell
powershell -NoLogo -NoProfile -File `
  .\scripts\Test-Windows10ExplorerCaptionOverlay.ps1
```

The audit enforces the native import allowlist, own-HWND style boundary,
foreground/TTL/retirement behavior, negative mutation receipt fields, exact
profile capability and Release build. Its offline policy model covers both TTL
limits, the explicit confirmation gate, DPI-aware native-control reservation
and narrow-window width clamping.

## Neural Void visual contract v2

The current canary is deliberately a thin system-surface treatment rather than
an application header. Its active signal is authored WPF vector geometry. A
small duplicate vector receives a bounded blur pass behind the sharp core, so
the glow remains post-processed and the core remains resolution-independent.
There are no bitmap assets, gradient strips, Unicode icon glyphs or imitation
caption buttons. The visible `CANARY mm:ss` label makes automatic retreat
legible without adding an interactive panel.

This v2 visual contract has passed offline build, policy and source-boundary
checks. The live evidence below applies to v1; v2 must not be described as
visually verified until a fresh, explicitly approved single-window preview has
been captured before, during and after expiry.

## Bounded visual preview

First use the read-only caption plan to obtain a fresh admitted handle. Then
run the overlay against that exact handle:

```powershell
dotnet run --project `
  .\src\platforms\windows10\Jarvis.Win10.ExplorerCaptionOverlay\Jarvis.Win10.ExplorerCaptionOverlay.csproj `
  -c Release --no-build -- show `
  --expected-window-handle 0x123456 `
  --ttl-seconds 30 `
  --confirm-owned-explorer-caption-overlay-preview
```

The process emits a JSON receipt after the window closes. A valid receipt must
report `ownedWindowOnly`, `mouseTransparent` and `noActivate` as `true`, and
all Explorer mutation, injection, restart, registry and module-activation
fields as `false`.

Visual verification must capture three states: the admitted Explorer before
launch, the owned overlay while Explorer is foreground, and the Explorer after
TTL expiry. The overlay should disappear when another application is
foreground and reappear only if the same admitted Explorer window returns to
the foreground before expiry.

## Verified live result

The final 30-second run on 2026-07-31 bound `0x7800EA`, PID 4360 and TID
10324. The target was a Win10 folder window in a separate observed Explorer
process, so the overlay gate accepted only
`explorer-root-pid-not-desktop-shell`; every other exact caption gate check
passed.

The v1 session recorded 273 foreground samples, zero hidden samples and one
positioning operation. The visible state changed 35,744 pixels, all within the
1117x32 caption band. The before and post-TTL images have the same SHA-256 and
zero differing pixels. The session receipt records an owned, mouse-transparent,
non-activating window and denies Explorer mutation, injection, restart,
registry mutation and module activation.

Evidence is stored under
`docs/receipts/win10-explorer-caption-overlay-live-20260731T111823246Z`.
