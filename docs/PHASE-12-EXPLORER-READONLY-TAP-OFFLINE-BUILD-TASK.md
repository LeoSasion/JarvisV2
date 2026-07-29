# Phase 12 — Explorer Read-only TAP Offline Build

Status: **OFFLINE TAP BUILD COMPLETE — DLL NEVER LOADED**

## Outcome

Phase 12 produces the first standalone AMD64 TAP-shaped DLL and controller
executable for the Explorer XAML track. Both are intentionally inert:

- the TAP live-connection macro is fixed to `0`, and any other value is a
  compile error;
- `IObjectWithSite::SetSite` always returns `E_ACCESSDENIED`;
- the controller accepts only `--describe`, accepts no PID or execution
  argument, has an endpoint limit of zero and cannot load the TAP;
- validation compiles and inspects the DLL as a file but never loads it.

This is an ABI and binary-shape milestone, not a live read-only probe.

## Delivered components

`src/platforms/windows11/Jarvis.ExplorerTapReadOnly` contains:

1. a strict `JARVIS2-XAML-RO-V1:` initialization-data codec for the 616-byte
   Phase 11 bind request;
2. an exact in-target identity verifier for the current PID, non-desktop Shell
   PID, TID, HWND, `CabinetWClass`, exact `C:\` title and process start time;
3. a COM class factory and `IObjectWithSite` TAP object;
4. an `IVisualTreeServiceCallback2` implementation that can only count a
   bounded number of events;
5. a describe-only controller that records the eventual exact-target and
   existing-consumer rejection policy.

The target verifier and callback exist in the DLL on disk but are unreachable
in this phase because `SetSite` refuses the diagnostics site.

## Protocol boundary

The initialization payload is exactly 1,251 UTF-16 characters:

- 19-character versioned prefix;
- 1,232 uppercase hexadecimal characters encoding the exact fixed-width bind
  request;
- no arbitrary operation, selector, property name, code pointer or variable
  data shape.

The parser rejects:

- null and undersized outputs;
- length or prefix drift;
- lowercase or non-hex data;
- ABI and structure-size drift;
- zero or desktop-Shell target identities;
- missing PID, TID, HWND, process-start, title or tree-generation identities;
- missing capability, plan, selector or nine styled-value hashes;
- invalid issue/expiry windows;
- anything other than the fixed 60-second, three-surface, three-property plan.

The portable protocol harness requires the exact 38/38 matrix.

## COM and binary boundary

The TAP DLL exports exactly:

- `DllCanUnloadNow`;
- `DllGetClassObject`.

It does not export `DllMain` or registration helpers. The class factory:

- rejects aggregation;
- clears output pointers before work;
- contains an exception firewall;
- tracks balanced `LockServer` calls and rejects underflow.

The offline PE inspector verifies AMD64 PE32+, the exact export set, and the
complete normal import tables of both DLL and controller. It rejects
XAML-runtime, diagnostics-loader, remote-process, injection, hook,
termination, registry-write and service-start imports.

## Existing diagnostics consumer policy

The reviewed upstream File Explorer Styler contains broad endpoint retries and
can coexist by hooking another diagnostics consumer. JarvisV2 will not copy
that behavior. The Phase 12 contract records:

- existing consumer: reject;
- endpoint attempts: zero;
- displacement or hook: forbidden.

A future live-capable design must first implement a bounded, inspectable
consumer/endpoint admission check. It may fail closed; it may not hook, block
or displace another consumer.

## What remains unavailable

Phase 12 cannot:

- call `InitializeXamlDiagnosticsEx`;
- load the TAP into any process;
- accept or discover an Explorer PID;
- connect to `IXamlDiagnostics`;
- advise the visual-tree callback;
- resolve an instance handle;
- read or fingerprint a XAML property;
- set, clear or restore a property;
- start Windhawk or restart Explorer.

Accordingly, the machine contract freezes:

- `propertyReadSupported=false`;
- `executionSupported=false`;
- `readyForLiveConnection=false`;
- `readyForExactApproval=false`;
- `activationPermitted=false`;
- `liveExplorer=not-run`;
- `mutationPerformed=false`.

## Evidence

`scripts/Test-ExplorerReadOnlyTap.ps1` performs:

- source and machine-contract audits;
- strict PowerShell PE export/import parsing;
- warning-as-error portable builds of the protocol harness, describe-only
  controller and TAP DLL;
- 38/38 deterministic protocol scenarios;
- controller receipt verification;
- disk-only DLL inspection followed by deletion of the temporary build
  directory.

Public CI runs the source and contract portion with `-StaticOnly`; the local
full project gate runs the fixed portable compiler build.

## Next gate

The next development phase should add an offline controller admission model
for one caller-supplied PID and one bounded endpoint. It should model
pre-existing-consumer detection, exact binary identity, one-shot plan
consumption and failure receipts without linking or calling XAML Diagnostics.

Only after that model and a read-only property-fingerprint TAP implementation
are independently reviewed should the project consider a new live-read-only
approval package. Building this DLL does not grant permission to load it.
