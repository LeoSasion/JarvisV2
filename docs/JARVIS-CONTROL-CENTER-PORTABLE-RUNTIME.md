# JARVIS Control Center portable runtime

The Control Center can now run outside the Codex workspace from one portable
Windows x64 directory. The package is self-contained for .NET and bundles the
reviewed Node executable plus a flat, hash-receipted 34-package runtime closure
for the exact Pi 0.82.1 core modules used by JARVIS. It also bundles the fixed
Git for Windows runtime used by reviewed iteration repository gates.

## Package layout

```text
jarvis-control-center.exe
jarvis-pi-agent-desktop-bridge.exe
runtime/
  node/node.exe
  git/
    cmd/git.exe
    LICENSE.txt
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
  -GitPath C:\portable\git\cmd\git.exe `
  -DotnetPath C:\portable\dotnet\dotnet.exe `
  -OutputPath .\artifacts\jarvis-control-center-portable-qa

pwsh -File .\scripts\Test-JarvisControlCenterPackage.ps1 `
  -PackagePath .\artifacts\jarvis-control-center-portable-qa
```

The builder refuses existing destinations and paths outside `artifacts`. The
receipt binds hashes for both executables, Node, the full Git runtime closure
plus its license, the
packaged host contract, Pi manifest/lock/host and package README. It also
records every runtime package name, version and manifest hash. The audit
re-hashes them, checks the exact closure, rejects credential-like artifacts,
runs the packaged sidecar's
offline inspection and then runs the packaged bridge's full
multi-turn/read-tool/cancellation/checkpoint/shutdown probe.

At application startup, packaged resolution re-hashes every critical receipt
entry, every bundled Git file and every recorded runtime package manifest. It
verifies each manifest's name/version pair and rejects missing, added, changed
or reparse-pointed packaged layouts before developer fallback can be considered.

## Launch

Open `jarvis-control-center.exe`, choose `START PI SESSION`, select one local
workspace and choose a provider. Local diagnostic is the safe default. The
launcher verifies the workspace and the packaged receipt before the existing
window transitions into the Pi conversation. No command line is required.

The package README also retains the automation equivalent:

```powershell
jarvis-control-center.exe --conversation `
  --workspace C:\absolute\workspace `
  --provider local
```

For the opt-in Responses provider, first choose `CONFIGURE OPENAI`, protect the
key, then select `OPENAI RESPONSES` in `START PI SESSION`. The key is stored
under the current Windows user outside the portable directory. The
`--provider openai` command-line mode remains available for automation.

## Safety boundary

Packaging, auditing and conversation launch do not install software, inject a
module, configure Windhawk, clear the JARVIS2 kill switch, modify the registry,
restart Explorer or enable shell/direct mutation tools. It includes the
review-gated text workflow: `propose_edit`, `propose_patch`,
`propose_create_file` and `propose_change_set` stage without writing, and only the desktop owner can
approve an exact replacement, a 2–8-hunk single-file exact patch, or an
exclusive new UTF-8 file once against its reviewed before state, or an ordered
two-to-four-file change set as one whole-set decision. Patch hunks
must be distinct, uniquely matched and non-overlapping, and their combined
review text is capped at 16 KiB; a complete change-set review is capped at
32 KiB and uses strict startup recovery to converge to all-before or
all-committed-after state without claiming simultaneous visibility. The
desktop can also arm a four-edit, six-hour reviewed iteration from a clean Git
HEAD. It stores CurrentUser-DPAPI receipts and continues reasoning only after
each owner approval passes the fixed Git, strict UTF-8, tracked/untracked diff
and structured-text gate, then the owner separately approves the exact Node
test profile pinned in that HEAD. The bundled Node executable is launched
directly without a shell, with bounded time/output and full pre/post repository
revalidation; unattended approval and Pi process access remain unavailable.
The portable receipt therefore
records the bundled `runtime\git\cmd\git.exe` direct-process gate alongside
the direct trusted-validation boundary, Node and Pi runtimes. No host Git
installation is required by the package. The receipt fixes
`activationPermitted=false`, `liveExplorer=not-run` and
`systemMutationPerformed=false`.
