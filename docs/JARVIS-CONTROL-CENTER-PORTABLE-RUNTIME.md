# JARVIS Control Center portable runtime

The Control Center can now run outside the Codex workspace from one portable
Windows x64 directory. The package is self-contained for .NET and bundles the
reviewed Node executable plus a flat, hash-receipted 34-package runtime closure
for the exact Pi 0.82.1 core modules used by JARVIS.

## Package layout

```text
jarvis-control-center.exe
jarvis-pi-agent-desktop-bridge.exe
runtime/
  node/node.exe
  pi-agent/
    package.json
    pnpm-lock.yaml
    src/host.mjs
    node_modules/@earendil-works/...
README.txt
package-receipt.json
```

`DesktopRuntimeBootstrap` always tries this complete packaged layout first.
Only development builds fall back to an installed Pi project plus
`JARVIS2_NODE_PATH` or `node.exe` on `PATH`. A partial package fails closed;
it is not mixed with developer components.

## Build and audit

Use a new output directory under `artifacts`:

```powershell
pwsh -File .\scripts\New-JarvisControlCenterPackage.ps1 `
  -NodePath C:\portable\node\node.exe `
  -DotnetPath C:\portable\dotnet\dotnet.exe `
  -OutputPath .\artifacts\jarvis-control-center-portable-qa

pwsh -File .\scripts\Test-JarvisControlCenterPackage.ps1 `
  -PackagePath .\artifacts\jarvis-control-center-portable-qa
```

The builder refuses existing destinations and paths outside `artifacts`. The
receipt binds hashes for both executables, Node, the packaged host contract,
Pi manifest/lock/host and package README. It also records every runtime package
name, version and manifest hash. The audit re-hashes them, checks the exact
closure, rejects credential-like artifacts, runs the packaged sidecar's
offline inspection and then runs the packaged bridge's full
multi-turn/read-tool/cancellation/checkpoint/shutdown probe.

At application startup, packaged resolution re-hashes every critical receipt
entry and every recorded runtime package manifest, verifies each manifest's
name/version pair and rejects partial or reparse-pointed packaged layouts before
developer fallback can be considered.

## Launch

The package README contains the same commands:

```powershell
jarvis-control-center.exe --conversation `
  --workspace C:\absolute\workspace `
  --provider local
```

For the opt-in Responses provider, first open the executable without arguments
and choose `CONFIGURE OPENAI`; then relaunch with `--provider openai`. The key
is stored under the current Windows user outside the portable directory.

## Safety boundary

Packaging, auditing and conversation launch do not install software, inject a
module, configure Windhawk, clear the JARVIS2 kill switch, modify the registry,
restart Explorer or enable mutation tools. The portable receipt therefore
fixes `activationPermitted=false`, `liveExplorer=not-run` and
`systemMutationPerformed=false`.
