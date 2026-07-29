# Windows 10 native-style probe

The first Windows 10 backend slice is
`Jarvis.Win10.NativeStyleProbe`. It proves the platform boundary with a real
window owned by the probe process. It does not discover, message, restart or
modify Explorer.

## Commands

Build with the SDK pinned by `global.json`, then run one command:

```powershell
dotnet build `
  .\src\platforms\windows10\Jarvis.Win10.NativeStyleProbe\Jarvis.Win10.NativeStyleProbe.csproj `
  --configuration Release

dotnet run `
  --project .\src\platforms\windows10\Jarvis.Win10.NativeStyleProbe `
  --configuration Release `
  --no-build `
  -- inspect

dotnet run `
  --project .\src\platforms\windows10\Jarvis.Win10.NativeStyleProbe `
  --configuration Release `
  --no-build `
  -- verify-owned-window

dotnet run `
  --project .\src\platforms\windows10\Jarvis.Win10.NativeStyleProbe `
  --configuration Release `
  --no-build `
  -- show
```

- `inspect` reads exact host identity and DWM visual state and prints JSON. It
  creates no window and performs no mutation.
- `verify-owned-window` creates an off-screen window owned by the process,
  applies and resets `DWMWA_USE_IMMERSIVE_DARK_MODE`, closes the window and
  prints a JSON receipt.
- `show` opens the interactive probe. Its three presets affect only that
  window. The same owned client surface now consumes the shared Neural Void
  RGB frame with A/C/D shortcuts, continuous hue and four effects. Closing it
  releases all style state.

## Exact admission

`config/windows10-host-profiles.json` is embedded into the executable.
Admission requires an exact match for:

- Windows build and UBR;
- architecture and installation type;
- Explorer product version, file version, size and SHA-256.

The first profile is `win10-22h2-19045.6466-x64`. It grants only two
capabilities: read system DWM state and set the dark-caption attribute on an
owned HWND. It does not authorize an Explorer module or live activation.

## Visual sharing boundary

The probe carries the same Jarvis graphite, cyan, typography and density
intent used by the common control surface. The platform translation is
deliberately small:

- Win10 uses its supported dark native caption plus a WPF-owned client
  surface. RGB frames color only the client surface; Win10 does not expose the
  reviewed Win11 caption-color attributes.
- Win11 keeps its separately reviewed corner, caption-color and system
  backdrop implementation.

The tokens may move into `src/common` after both backends exercise the same
contract. The native DWM calls remain in their versioned platform adapters.

## Audit

Run the complete local gate on the reviewed Win10 host:

```powershell
pwsh -NoLogo -NoProfile -File `
  .\scripts\Test-Windows10NativeStyleProbe.ps1
```

CI uses `-StaticOnly`; it builds and audits the boundary without trying to
match the CI runner to the exact local host.
