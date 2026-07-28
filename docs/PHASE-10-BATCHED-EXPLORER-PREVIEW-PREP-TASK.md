# Phase 10 — Batched Explorer Preview Preparation

Status: **OFFLINE DEVELOPMENT COMPLETE — VISUAL APPROVAL NOT REQUESTED**

## User outcome

This phase batches three development items before asking the user to inspect
another visual preview:

1. pin and audit the GPL Windows 11 File Explorer Styler source;
2. compile a version-bound three-surface selector candidate and add an exact,
   read-only Explorer UI Automation topology probe;
3. produce a 60-second preview review plan with complete journaling,
   before/during/after screenshots and strict reverse restoration.

No live Explorer inspection, XAML Diagnostics connection, style write,
Windhawk launch, module load, Explorer restart, registry change or system-file
change is performed by this phase.

## GPL upstream baseline

The selected upstream is
`ramensoftware/windhawk-mods/mods/windows-11-file-explorer-styler.wh.cpp`
version 1.5:

- audited commit:
  `109589023dde428deaee2fe80e4ce446283a7935`;
- Git blob: `6f67b714c271db1235a5f937c30c5cae55b180bf`;
- source size: `326922` bytes;
- SHA-256:
  `ECD6189A76439518E84938F4CA42FDB7F78AA1CCE3151EE0FE93638918D2DCED`;
- license: GPL-3.0.

JarvisV2 adopts the documented selector grammar and reviewed target names. It
does not vendor or copy the Windhawk loader/service, broad injector, DLL
entrypoint, hook installation, COM/GIT lifecycle,
`InitializeXamlDiagnosticsEx` bootstrap, blur brush, telemetry, custom-code
execution or Explorer restart path.

## Candidate selector profile

`config/explorer-frame-selector-candidate.json` binds the existing
`win11-25h2-26200.8875-x64` compatibility profile and exact Explorer identity.
It contains exactly three targets:

- `tab-strip`: an upstream-derived `FileExplorerTabControl` /
  `TabContainerGrid` parent-chain candidate;
- `command-bar`: an upstream-derived `CommandBarControl` /
  `CommandBarControlRootGrid` parent-chain candidate;
- `navigation-pane`: an inferred `NavigationView` root candidate that must be
  confirmed by read-only evidence.

The compiler accepts only class/name parts, explicit parent chains and a
bounded middle wildcard. Property predicates, visual-state selectors,
comments, newlines, edge wildcards and global selectors are rejected.
Every target requires exactly one match. Compilation emits selector
fingerprints and `readyForReadOnlyDiscovery=true`, while keeping
`readyForPreview=false`, `readyForExactApproval=false`,
`activationPermitted=false`, and `liveExplorer=not-run`.

The navigation-pane candidate is deliberately not presented as verified.
Neither upstream theme presence nor a UI Automation hint proves the exact
live XAML instance.

## Exact read-only topology probe

`Jarvis.ExplorerSurfaceProbe` is an actual Windows read-only executable, but it
is not run in this phase. Its only operation is `inspect-exact`, which requires
the caller to provide:

- one exact nonzero HWND;
- exact PID and TID;
- exact full title;
- exact UTC process start time;
- the expected desktop Shell PID.

It rejects any target that is not `CabinetWClass`, is not `explorer.exe`,
does not match every supplied identity field, or shares the desktop Shell PID.
It never enumerates windows or chooses a target by title alone.

After admission it reads at most 2,048 UI Automation raw-view nodes to depth
14. The receipt contains only bounded class names, AutomationIds, control
types, structure keys and a topology hash. It omits UI element names, file
paths, visible text, bounding rectangles and user content. Surface matches are
labelled `uia-topology-hint-not-xaml-proof`; the output can narrow later XAML
discovery but cannot authorize a style write.

The probe imports only seven read-only USER32 functions for window identity.
It contains no window mutation, message send, UIA action pattern, process
launch/termination, process injection, hook, registry or service API.

## Unified 60-second review plan

`Jarvis.ExplorerPreviewModel` accepts only:

1. a passing candidate compilation;
2. a profile-hash-bound read-only discovery receipt no older than two minutes;
3. an exact separate Explorer target;
4. one distinct matched instance for each surface;
5. all three original property values for all three surfaces.

A valid synthetic receipt produces this non-executable order:

1. verify exact target identity;
2. capture the before screenshot;
3. journal every original property;
4. apply tab-strip, command-bar, then navigation-pane;
5. capture the during screenshot;
6. wait until the 60-second deadline;
7. restore navigation-pane, command-bar, then tab-strip;
8. verify all originals;
9. capture the after screenshot;
10. close the temporary Explorer window.

The plan never self-approves. It does not contain a loader or live property
transport and always returns `readyForExactApproval=false`.

## Deterministic evidence

- selector profile schema validation;
- real candidate compilation against `config/compatibility.json`;
- 43/43 profile and preview-plan fault scenarios;
- warning-free Release builds for the compiler/planner and read-only probe;
- static import and mutation audits;
- full project and publication-boundary validation.

The fault matrix covers upstream drift, host fingerprint drift, malformed
selectors, role/property drift, stale evidence, desktop-Shell targeting,
PID/TID/HWND/title/start-time drift, reused instances, non-unique matches,
selector mismatch, incomplete original snapshots, mutation claims and
apply/restore order.

## Remaining gate before one visual approval

The next technical stage must implement and portable-build a standalone,
single-PID/single-TID XAML transport. Windhawk remains quarantined and is not an
acceptable live transport because its base runtime is not restricted to the
one temporary Explorer process.

Before the one combined visual approval, JarvisV2 must show:

- a fresh compatibility report;
- exact probe, transport and selector-profile SHA-256 values;
- a read-only receipt for the temporary `C:\` Explorer window;
- unique XAML matches for all three surfaces;
- a complete original-value journal;
- a visible recovery command and recovery terminal;
- one exact command that applies the three-surface pack for 60 seconds,
  captures screenshots, restores in reverse and closes the temporary window.

Only that final command needs visual approval. This document, the candidate
profile, UI Automation hints and synthetic preview plan do not authorize it.
