# Phase 11 — Explorer XAML Transport Core

Status: **OFFLINE TRANSPORT CORE COMPLETE — NO TAP DLL OR LIVE CONNECTION**

## Outcome

Phase 11 turns the Phase 10 preview order into a fixed-width, portable native
transport contract and deterministic state machine. It is the safety core for a
future standalone XAML Diagnostics controller/TAP pair.

This phase delivers three related artifacts:

1. a machine-readable transport contract that records the real
   `InitializeXamlDiagnosticsEx` boundary;
2. a native ABI bound to one exact Explorer PID, TID, HWND, process start time,
   title hash, visual-tree generation, selector-profile hash and preview-plan
   hash;
3. an executable offline fault harness covering capability replay, identity
   drift, partial apply, timeout and strict reverse restoration.

There is no TAP DLL, COM export, XAML Diagnostics call, loader, injection,
window discovery, Explorer connection or property write in this phase.

## Why the transport cannot be a normal out-of-process style API

Microsoft documents `InitializeXamlDiagnosticsEx` as the entry point for a
XAML Diagnostics session. The caller supplies a PID, a diagnostics DLL, a TAP
DLL and a TAP CLSID. The TAP DLL is injected into the named process, and its
`IObjectWithSite` object receives a site implementing `IXamlDiagnostics`.

This means an eventual File Explorer implementation is a controlled in-process
component even when its controller is a separate executable. Calling the API
is itself a live module-load event and must remain behind a fresh exact
approval. A UI Automation hint, successful portable build or offline state
machine cannot substitute for that approval.

The reviewed GPL File Explorer Styler confirms the practical WinUI 3 shape:

- it resolves the Explorer-side diagnostics export;
- it creates an `IObjectWithSite` TAP;
- the TAP obtains `IXamlDiagnostics`;
- a visual-tree callback receives instance handles;
- the implementation resolves those handles to WinUI objects.

The upstream implementation also tries a large range of endpoint names, hooks
other diagnostics consumers and can execute work on Explorer window threads.
JarvisV2 does not copy those broad mechanisms.

## Machine contract

`config/explorer-xaml-transport-contract.json` records the following frozen
boundary:

- caller-supplied exact PID only;
- no process or window enumeration;
- the desktop Shell process is forbidden;
- every command repeats and revalidates the exact target identity;
- one consumer, one reviewed TAP DLL and one target process;
- one 120-second maximum capability;
- one 60-second preview;
- no self-approval or replay;
- exactly three surface roles and three allowed properties;
- exactly nine original-value journal entries;
- no arbitrary selector, property, XAML or operation registration;
- no Windhawk runtime, global injection, thread-hook injection, Explorer
  restart, process termination, registry write or service start.

The accompanying JSON schema freezes the locked-state fields:

- `executionSupported=false`;
- `readyForLiveConnection=false`;
- `readyForExactApproval=false`;
- `activationPermitted=false`;
- `liveExplorer=not-run`;
- `mutationPerformed=false`.

## Portable ABI

`jarvis_explorer_transport_contract.h` defines ABI v1 using only fixed-width
types. The future controller/TAP boundary must bind:

- Explorer PID and desktop Shell PID;
- exact window TID and HWND;
- Explorer process start time;
- SHA-256 visual-tree generation identity;
- SHA-256 of the exact window title;
- 256-bit session nonce;
- SHA-256 of the selector profile and preview plan;
- the exact three selector SHA-256 values;
- the exact nine styled-value SHA-256 values;
- monotonic issue and expiry times;
- a strictly monotonic command sequence.

The ABI exposes only the state-machine operations needed by the reviewed
preview:

1. bind the one-shot capability;
2. publish exactly one instance for each surface in fixed order;
3. journal nine original values in fixed order;
4. record the result of each future property write;
5. latch restore-required when the 60-second deadline is reached;
6. record strict reverse restoration;
7. quiesce only when no applied value remains.

The model response always declares execution, activation, live access and
mutation false. `simulated_mutation_count` is internal harness accounting and
never becomes a live claim.

## Failure semantics

Before the first simulated write, any malformed size, ABI, target identity,
capability, selector instance, journal entry or sequence gap blocks the model.

After any simulated write:

- identity or generation drift latches `RESTORE_REQUIRED`;
- a partial apply stops all further apply operations;
- timeout latches `RESTORE_REQUIRED`;
- quiesce is rejected while applied values remain;
- restoration accepts only the exact original hash for the last applied slot;
- a failed restoration retains `RESTORE_REQUIRED`;
- no model path restarts or terminates Explorer.

This preserves the distinction between “stop issuing new writes” and
“successfully restored every original”.

## Deterministic evidence

`scripts/Test-ExplorerTransportModel.ps1`:

- parses and audits the machine contract and schema;
- rejects live XAML, loader, hook, process, window, service and registry APIs;
- checks exact ABI sizes and identity/capability fields;
- verifies the three-by-three journal and reverse-restore invariants;
- compiles the model and harness using the portable Clang toolchain;
- requires the exact 85/85 fault matrix;
- verifies every receipt remains non-live and non-authorizing.

## Next gate

The next implementation phase may build, but must not load, a standalone
read-only TAP DLL and exact-PID controller. That source must:

- expose only the reviewed COM class needed by XAML Diagnostics;
- parse initialization data into this ABI without accepting arbitrary code or
  arbitrary operations;
- revalidate PID, TID, HWND, title, start time and generation inside the target
  before publishing any element handle;
- locate only the three profile-bound selectors;
- return original-value fingerprints without setting or clearing a property;
- reject an existing diagnostics consumer rather than hook, block or displace
  it;
- have a bounded endpoint strategy rather than a 10,000-name retry loop;
- portable-build to exact hashes with zero warnings.

Loading that future DLL would still be a live injection event. It requires a
fresh compatibility report, reviewed hashes, a recovery terminal and the
user's exact current approval. Phase 11 grants none of those permissions.
