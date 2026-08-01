# Windows 10 bounded Explorer caption session

`Jarvis.Win10.ExplorerCaptionSession` is a bounded single-window backend for
the standard Win10 Explorer title bar. It requests only
`DWMWA_USE_IMMERSIVE_DARK_MODE = 20` for 10–60 seconds.

This is not DLL injection. It does not hook Explorer, access process memory,
start Windhawk, restart or terminate Explorer, change the registry, style
Explorer content, or touch the taskbar.

## Apply boundary

Before SET, the session requires:

1. the exact Win10 host profile and fresh read-only Shell inventory;
2. an explicitly supplied `CabinetWClass` HWND present in that inventory;
3. exact HWND/PID/TID/class equality on a second pre-apply inspection;
4. a readable original boolean value;
5. armed `disabled.flag` and absent `active-module.txt`;
6. the profile's bounded non-module caption-preview capability;
7. a 10–60 second TTL;
8. `--confirm-live-single-explorer-dark-caption`.

The original value, exact target and TTL are written through to a
path-confined journal before the first SET attempt. SET is followed by a
nonclient-only
`RedrawWindow(RDW_INVALIDATE | RDW_FRAME | RDW_NOCHILDREN)` request,
`DwmFlush`, and a readback of attribute 20. The redraw request does not
activate, move, resize or reorder the target.

## Recovery

TTL expiration, Ctrl+C and exceptions enter the same `finally` rollback. The
stored target is checked again with `IsWindow`, `GetClassNameW` and
`GetWindowThreadProcessId` before the original value is restored and read
back. Rollback requests the same bounded nonclient repaint after restoring the
original value.

The emergency rollback command uses the journaled target identity directly so
that later profile drift cannot prevent recovery:

```powershell
dotnet run `
  --project .\src\platforms\windows10\Jarvis.Win10.ExplorerCaptionSession `
  --configuration Release `
  --no-build `
  -- rollback `
  --session "$env:LOCALAPPDATA\JARVIS2\ExplorerCaption\active-session.json" `
  --confirm-live-single-explorer-dark-caption-rollback
```

If the recorded HWND no longer has the same class/PID/TID identity, rollback
sends no DWM write to a replacement target and records `target-retired`.

## First live observation

The first approved live run was
`20260731T080450367Z-848c60dd`. DWM attribute readback passed from `0` to `1`,
but a comparison of the top 32 rows found zero changed pixels out of 36,448.
The exact target HWND retired before TTL rollback; the session correctly sent
no write to a replacement handle, and the Explorer process remained PID 1244.
This run is therefore recorded as
`api-readback-passed-visual-diff-failed-target-retired`, not as a visual
success.

## Redraw live observation

The separately approved redraw run was
`20260731T084924086Z-608182a4`. The same HWND remained exact throughout:
attribute `0 → 1 → 0`, apply and rollback repaint requests, and rollback
readback all passed. Nevertheless, the full before, during and after PNG files
have the same SHA-256, and the 36,448-pixel title-bar sample again had zero
changed pixels.

Read-only theme inspection reported `AppsUseLightTheme=1`. Attribute 20 allows
the standard frame to honor immersive dark mode; it is not a documented
arbitrary caption-color override for a light-themed application. The exact
profile therefore no longer grants
`run-bounded-single-explorer-dark-caption-preview`. Read-only caption
inspection remains available, but another write must stay blocked until a new
documented Win10-compatible design is reviewed. System theme registry changes,
undocumented UxTheme calls and injection are not fallback paths.

Explorer content and taskbar changes remain separate later gates.

## Build receipt

Generate the fixed-toolchain receipt with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File `
  .\scripts\New-Windows10ExplorerCaptionBuildReceipt.ps1 `
  -DotnetPath .\artifacts\toolchains\dotnet-8.0.423\dotnet.exe
```

The generated receipt remains under `artifacts/`; it binds the exact
transitive source/config set, portable toolchain hash and all four managed
assemblies. `liveExplorer=not-run` remains the repository's DLL/module
activation boundary; the bounded non-module observations are recorded
separately in the exact host profile and live-observation artifacts.
