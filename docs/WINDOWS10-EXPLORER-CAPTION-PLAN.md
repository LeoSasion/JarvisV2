# Windows 10 single-Explorer caption plan

`Jarvis.Win10.ExplorerCaptionPlan` is the first separate Windows 10 Explorer
backend slice. It reads one standard Explorer window's current
`DWMWA_USE_IMMERSIVE_DARK_MODE` value and prepares a non-executable,
10–60 second preview plan.

This project does not import `DwmSetWindowAttribute`. It cannot write a
window, inject a DLL, start Windhawk, restart Explorer, change the registry or
style Explorer content.

## Exact read gate

Every host command requires:

1. exact profile `win10-22h2-19045.6466-x64`;
2. a passing read-only desktop/taskbar topology inventory;
3. one visible `CabinetWClass` root, selected by an exact HWND when multiple
   candidates exist;
4. that selected root's PID equals the desktop Shell PID;
5. recorded HWND, PID, TID, rectangle and topology SHA-256;
6. a successful boolean read of DWM attribute 20;
7. an armed `%LOCALAPPDATA%\JARVIS2\disabled.flag`;
8. no `%LOCALAPPDATA%\JARVIS2\active-module.txt`.

If multiple Explorer folder windows are open and no exact HWND is supplied,
the gate fails closed instead of choosing one by title or enumeration order.
The supplied HWND must already exist in the fresh read-only inventory.

## Commands

```powershell
dotnet run `
  --project .\src\platforms\windows10\Jarvis.Win10.ExplorerCaptionPlan `
  --configuration Release `
  --no-build `
  -- inspect `
  --expected-window-handle 0x...

dotnet run `
  --project .\src\platforms\windows10\Jarvis.Win10.ExplorerCaptionPlan `
  --configuration Release `
  --no-build `
  -- plan-preview `
  --expected-window-handle 0x... `
  --ttl-seconds 30
```

The plan records the original boolean value and the exact future recovery
contract. It keeps `previewExecutionSupported=false`,
`mutationSupported=false`, `activationPermitted=false` and
`mutationPerformed=false`.

The separate write session now implements the durable journal, second exact
target gate, bounded SET, nonclient repaint request and `finally` rollback.
Two live observations produced successful attribute readback but no title-bar
pixel change. The second also proved the nonclient repaint and exact rollback,
while `AppsUseLightTheme=1`. The exact profile now revokes the caption-write
capability, so planning remains read-only until a different documented design
is reviewed.
Explorer content and taskbar writes remain later gates.
