# Windows 10 exact-thread callback package (offline only)

This boundary is hard-disabled for live Explorer use. Native collector unload,
Hook removal and callback-drain proof are not closed, so no collector executable
is published or run.

The package contains exactly two files:

- `jarvis-win10-explorer-callwndproc-bridge.dll`;
- `package-receipt.json`.

The DLL is a disk-only, empty pass-through callback envelope. Its receipt fixes
`offlineOnly=true`, `collectorExecutablePublished=false`,
`activationPermitted=false`, `liveExplorer=not-run` and
`mutationPerformed=false`. The source identity is the exact eight-file set used
to build and describe the DLL: the Win10 BridgeCore sources, the Win10 empty
CallWndProc sources and the package builder. Transport and collector research
sources are not package inputs.

Build and offline verification use:

```powershell
$package = '.\artifacts\win10-explorer-exact-thread-collector-local'
.\scripts\New-ExplorerExactThreadCollectorPackage.ps1 -OutputDirectory $package
.\scripts\Test-ExplorerExactThreadCollector.ps1 -PackageDirectory $package
```

`scripts/Invoke-ExplorerExactThreadCollectorLive.ps1` remains only as a
compatible official entry point. It returns nonzero with `result=blocked`
before resolving a package, touching `disabled.flag` or `active-module.txt`,
acquiring a mutex, inspecting Explorer or starting a process. If an explicit
`ControllerReceiptPath` is supplied, it may write only that blocked receipt.

The offline suite proves the two-file/no-EXE package boundary, exact source and
artifact hashes, native static contracts and portable harnesses. It also proves
that invoking the blocked entry preserves state bytes, starts no collector and
creates no Explorer module mapping. None of this is live Explorer evidence.
