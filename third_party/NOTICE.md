# Third-party notice and modification record

JarvisV2 is distributed under GPL-3.0. The full license text is in `/LICENSE`.
The public name is JarvisV2; stable runtime paths, module identifiers and
historical modification labels continue to use JARVIS2.

## Windows 11 Taskbar Styler

- Project: `ramensoftware/windhawk-mods`, mod by m417z and contributors
- Source: `mods/windows-11-taskbar-styler.wh.cpp` version 1.7
- Release commit: `cf0c6b1d2269380846d0da868898d35fc8678c06`
- Audited repository commit: `18615a32f83d3bcde2a32bd7ef648021b8100cc5`
- Original SHA-256: `E84FD55F81D6A0214EAE3BE6B7C89D1C1A2C95BCD7428B10F6C083F2B3E1FD21`
- License: GPL-3.0, declared in the source file
- Upstream: <https://github.com/ramensoftware/windhawk-mods>

JARVIS2 modifications on 2026-07-22:

- changed the mod identity and narrowed the target to `%SystemRoot%\explorer.exe` AMD64;
- added an exact OS build/UBR and host product-version gate before Hook registration;
- added `%LOCALAPPDATA%\JARVIS2\disabled.flag` as a fail-closed emergency switch;
- disabled the inherited theme statistics timer;
- removed the injected module's Explorer restart prompt/process launcher in favor of the out-of-process supervisor;
- added the original solid-brush JARVIS2 theme and made it the default;
- retained the upstream documentation and integrated themes for source history and attribution.

The inherited VisualTreeWatcher and blur-brush implementation credits
TranslucentTB's ExplorerTAP. TranslucentTB is GPL-3.0:
<https://github.com/TranslucentTB/TranslucentTB/tree/efcb774c0168e09f15a0c7a1db66eb7afdb575f0/ExplorerTAP>.

## Taskbar height and icon size

- Project: `ramensoftware/windhawk-mods`, mod by m417z
- Source: `mods/taskbar-icon-size.wh.cpp` version 1.3.7
- Release commit: `5d70208acc5a1f46d1c28439cb21c13f1079ec1d`
- Audited repository commit: `18615a32f83d3bcde2a32bd7ef648021b8100cc5`
- Git blob: `0ecb7d37a79365b31b8ef97161d727824de4a8b2`
- Canonical LF SHA-256: `F8FC11864877B1AD8DD975D4514E28608AA60E5A4924EFBAB363ACD54FEBBB57`
- Windows CRLF SHA-256: `FF080F8962E12D777C92A704C1BC462302D4514D8A54E79D912B34257B7DE692`
- License: GPL v3; JARVIS2 conservatively treats it as `GPL-3.0-only`
- Upstream: <https://github.com/ramensoftware/windhawk-mods>

JARVIS2 modifications on 2026-07-22:

- replaced the broad upstream module with one modern
  `TaskbarConfiguration::GetIconHeightInViewPixels()` hook;
- removed taskbar-height, button-width, tray, search, legacy shell, opcode
  scanner, object-offset, constant-patch and delayed-loader paths;
- made the feature disabled and stock-sized by default, with a 20-32 bound;
- added exact build, loaded-module path, product-version, file-size and SHA-256
  gates before private-symbol resolution;
- added the common emergency switch plus a latched runtime pass-through state;
- kept unloading free of refresh messages, Explorer restarts and persistent
  system changes.

## Windows 11 File Explorer Styler

- Project: `ramensoftware/windhawk-mods`, mod by m417z and contributors
- Source: `mods/windows-11-file-explorer-styler.wh.cpp` version 1.5
- Audited repository commit:
  `109589023dde428deaee2fe80e4ce446283a7935`
- Git blob: `6f67b714c271db1235a5f937c30c5cae55b180bf`
- Source size: `326922` bytes
- Source SHA-256:
  `ECD6189A76439518E84938F4CA42FDB7F78AA1CCE3151EE0FE93638918D2DCED`
- License: GPL-3.0, declared in the source file
- Upstream: <https://github.com/ramensoftware/windhawk-mods>

JARVIS2 Phase 10 reuse boundary:

- adopts the documented `Class#Name`, parent-chain and bounded wildcard
  selector grammar;
- records upstream Explorer candidates such as
  `FileExplorerExtensions.FileExplorerTabControl`,
  `Grid#TabContainerGrid`, `FileExplorerExtensions.CommandBarControl` and
  `Grid#CommandBarControlRootGrid`;
- models complete original-property capture and reverse-order restoration;
- does not vendor or copy the Windhawk loader, service, process injector,
  DLL entrypoint, hook installation, COM Global Interface Table lifecycle,
  `InitializeXamlDiagnosticsEx` bootstrap, blur brush, telemetry, custom-code
  execution or Explorer restart path;
- treats every selector as unverified until a bounded read-only receipt from
  the exact temporary Explorer window proves its match count.

## Windhawk

- Version: 1.7.3
- Commit: `b59b38cd77daec98830c0e5e2ad14a35c44f02a7`
- Architecture research commit:
  `61fc60dad607e6888d8de560d1b6add716f936c3`
- License: GPL-3.0
- Role: quarantined historical injection runtime and portable Clang build
  toolchain; no runtime source is vendored
- Upstream: <https://github.com/ramensoftware/windhawk>

Phase 6 uses the current upstream source only to document the service,
all-process injector and loader boundary. No Windhawk injector, service,
remote-memory, APC or remote-thread source was copied into the Explorer host
model.

## Pi coding agent

- Package: `@earendil-works/pi-coding-agent`
- Version: `0.82.1`
- Package integrity:
  `sha512-zbkAhoIuDPMF3pKuja0ajZabrMWU29FUMV9A/XMXT/XC1yXs5xt6t6t13GogQFsDrDqbFP4DkZQO1w8rWRAzYA==`
- License: MIT
- Upstream: <https://github.com/earendil-works/pi>

JarvisV2 uses Pi as an unmodified package dependency behind a separate Node.js
JSONL sidecar. The current transport probe imports and fingerprints the SDK
but refuses session creation, credentials and mutation tools. No Pi code is
loaded into Explorer.

## Reference-only projects

- Windows 11 Taskbar Styling Guide, commit `12a3c7900eb6581901548961b369e87541fcdd04`: selector research only. The repository has no declared license, so JARVIS2 vendors none of its images, icons, theme packages, or screenshots.
- eDEX-UI, commit `04a00c4079908788b371c6ecdefff96d0d9950f8`, GPL-3.0: visual-language reference only. No Electron runtime, JavaScript, CSS, fonts, images, or source files were copied.

Machine-readable locks are in `/config/upstream-lock.json`.
