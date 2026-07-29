# Windows 10 native tests

The first Win10-specific implementation is audited by
`scripts/Test-Windows10NativeStyleProbe.ps1`. The gate checks the exact host
profile, DWM import allowlist, owned-HWND proof, Win11 capability exclusions,
Release build and the local owned-window apply/reset roundtrip.

Future native harnesses belong in this directory. No test may load a module
into Explorer without a separately approved live gate.
