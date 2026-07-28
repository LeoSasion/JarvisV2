# Phase 13 — Explorer Read-only Admission and Fingerprint Core

Status: **OFFLINE MODELS COMPLETE — NO ENDPOINT ATTEMPT OR PROPERTY READ**

## Outcome

Phase 13 adds two fixed-width native cores without opening the Phase 12 live
compile gate:

1. a one-shot controller admission state machine for one exact caller-supplied
   Explorer identity and exactly one endpoint candidate;
2. a deterministic property-fingerprint state machine for the fixed three
   surfaces and three properties.

The controller binary contains the admission model, and the TAP DLL contains
the fingerprint model. Neither model is exported. The controller remains
`--describe`-only, its runtime endpoint-attempt limit remains zero, and the
TAP still rejects every diagnostics site with `E_ACCESSDENIED`.

## Admission contract

The 792-byte admission request contains the complete Phase 11 bind request and
SHA-256 identities for:

- the controller executable;
- the TAP DLL;
- the reviewed XAML Diagnostics DLL;
- the one endpoint name.

Admission requires:

- the exact non-desktop PID/TID/HWND/start-time/title/tree-generation bind;
- a current 120-second capability;
- zero observed diagnostics consumers;
- exactly one endpoint candidate;
- exactly two reviewed TAP exports;
- passing import and binary-identity policies;
- a ready recovery path;
- one available plan that is consumed by the first successful admission.

Any second attempt latches the model into `BLOCKED`, including a replay after a
successful first admission. The model contains no process or window
enumeration and does not inspect a live endpoint. A successful instance stores
the complete 616-byte bind request; the fingerprint core rejects any later
byte-level plan, selector, target, capability or styled-value drift.

## Fingerprint contract

The fingerprint model accepts exactly nine observations in
surface-major/property-minor order:

1. tab strip: Background, Foreground, BorderBrush;
2. command bar: Background, Foreground, BorderBrush;
3. navigation pane: Background, Foreground, BorderBrush.

Each observation repeats and validates:

- the exact target identity, including visual-tree generation;
- surface and property slot;
- the exact profile-bound selector hash;
- one nonzero instance handle, stable for all three properties of that
  surface and unique across the three surfaces;
- strict sequence;
- one of two canonical values: `null`, or a solid ARGB color with opacity
  represented as an integer from 0 to 1,000,000.

Arbitrary property names, runtime objects, strings, markup extensions,
resources, acrylic brushes and unknown value kinds are rejected. Unsupported
or noncanonical input latches `BLOCKED`.

## Fingerprint format

Each SHA-256 fingerprint is domain-separated by
`JARVIS2-XAML-PROP-V1` and binds:

- ABI version;
- surface and property slots;
- exact instance handle;
- exact selector SHA-256;
- visual-tree-generation SHA-256;
- canonical value kind, ARGB and opacity.

The implementation uses a self-contained allocation-free SHA-256 core. The
test harness independently freezes the first canonical vector as:

`00542DB9887A4CE9FA17AD0B42EC164D5E38FDD3BFE410D9517B2814CC264560`.

These hashes are evidence identifiers only. They are not sufficient to restore
an arbitrary XAML object and do not authorize property writes.

## Integration boundary

Phase 13 changes the disk binaries only:

- the describe-only controller links the offline admission and fingerprint
  model objects;
- the TAP DLL links the fingerprint object;
- PE inspection still requires only `DllCanUnloadNow` and
  `DllGetClassObject` exports;
- model functions are not DLL exports;
- `SetSite` remains a compile-time-guarded `E_ACCESSDENIED`.

There is still no call or dynamic resolution of
`InitializeXamlDiagnosticsEx`, no endpoint attempt, no TAP load, no
`IXamlDiagnostics` query, no callback subscription, no instance resolution and
no property read.

## Evidence and next gate

`scripts/Test-ExplorerReadOnlyAdmission.ps1` audits the contract and source,
warning-as-error builds the portable models, and runs the exact fault matrix.
`scripts/Test-ExplorerReadOnlyTap.ps1` separately rebuilds and inspects the
integrated controller/TAP binaries without loading the DLL.

The next gate is a source-only adapter design for translating a future
`IInspectable` property value into the two canonical forms. It must fail closed
for every unsupported runtime class and keep all COM access behind an
unreachable compile gate. Only after that adapter, endpoint admission evidence
and a fresh compatibility review exist may the project draft an exact
live-read-only approval command.

Phase 13 grants no permission to connect, inject, read Explorer properties or
modify the desktop.
