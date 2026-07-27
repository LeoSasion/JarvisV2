# Phase 9 — Explorer Frame Styler

Status: **OFFLINE MODEL COMPLETE — LIVE XAML CONNECTION NOT AUTHORIZED**

## Outcome

Phase 9 establishes the fail-closed core for visibly styling three native File
Explorer frame surfaces:

- the tab strip;
- the command bar;
- the navigation pane.

This phase does not connect to Explorer, load a DLL, initialize XAML
Diagnostics, install a hook, start Windhawk, restart Explorer, or change the
live desktop. The executable is a portable `net8.0` model that operates only on
an in-memory visual-tree fixture. Every receipt fixes
`executionSupported=false`, `activationPermitted=false`,
`liveExplorer=not-run`, and `mutationPerformed=false`.

## Why Phase 8 was not enough

The Phase 8 DWM session proved that a narrowly selected `CabinetWClass` window
can receive a temporary native border color and be restored. On the tested
Windows 11 Explorer, the application frame draws much of its own visible
chrome, so DWM caption and text attributes did not produce the requested
Jarvis visual language.

The next useful layer is therefore Explorer's native XAML visual tree. The
project must still avoid a replacement shell or an application-level canvas:
the future implementation will target native Explorer elements themselves.

## Safety contract

The offline model admits a candidate only when all identity fields are exact:

1. a nonzero target PID and TID are present;
2. the target PID differs from the desktop shell PID;
3. the HWND is nonzero and the class is exactly `CabinetWClass`;
4. the full window title matches the reviewed expected title;
5. Explorer is running as the separately launched test process;
6. process start time is UTC and the visual-tree generation is pinned.

Selectors must cover exactly the three approved roles. Each selector must come
from the offline candidate catalog, include an ancestor constraint, declare an
expected count of one, and match exactly one distinct node. The fixture class
and element names are deliberately marked `OfflineFixture.*`; they are not
claims about the current Windows 11 Explorer tree. Real selector names must
come from a future read-only discovery receipt.

Only these properties are admitted:

- `Background`;
- `Foreground`;
- `BorderBrush`.

Before the first simulated write, every original value is captured. Changes
are ordered by surface and property. Recovery restores the successfully
changed properties in strict reverse order. A partial apply immediately enters
the recovery path; a partial recovery remains `RestoreRequired` and can never
claim `Restored`. Generation drift blocks further work.

## State machine

```text
Cold
  -> Discovered
  -> Prepared
  -> Applied
  -> Restoring
  -> Restored

pre-mutation failure -> Blocked
post-mutation uncertainty -> RestoreRequired
partial apply -> reverse recovery -> Restored or RestoreRequired
```

## GPL source boundary

The existing GPL-3.0 `jarvis-native-taskbar` fork and its audited
Windows 11 Taskbar Styler upstream are used as design references for selector
semantics and the requirement to preserve original XAML property values.
Phase 9 does not copy their DLL entrypoint, loader, Windhawk hooks, COM Global
Interface Table lifecycle, `InitializeXamlDiagnosticsEx` bootstrap, process
targeting, or unload code into the offline model.

Keeping selection and restoration policy independent lets the project review
the risky transport later instead of inheriting a broad injection lifecycle by
accident. Any future derived runtime code remains subject to this repository's
GPL-3.0 license and the pinned provenance in `config/upstream-lock.json` and
`third_party/NOTICE.md`.

## Deterministic verification

`Jarvis.ExplorerFrameModel model-test` runs 29 scenarios covering:

- exact target identity and desktop-shell separation;
- missing, duplicate, untrusted, and ancestor-mismatched selectors;
- complete original-value snapshots and the property allowlist;
- deterministic apply order and strict reverse restore order;
- generation drift before apply or restore;
- partial apply, successful automatic recovery, and failed recovery;
- duplicate apply rejection and idempotent completed restore.

The model and its audit contain no Windows loader, process, service, registry,
P/Invoke, XAML Diagnostics, COM activation, hook-installation, or remote-memory
API.

## Next gate: read-only live discovery

The next task may produce a separate read-only discovery probe for one newly
opened `C:\` Explorer window. Before it is allowed to touch a live process, the
design must add:

1. a fresh compatibility and exact-window identity receipt;
2. a connection design with no property writes;
3. an explicit timeout and guaranteed disconnect;
4. proof that no desktop-shell Explorer instance is targeted;
5. a reviewed recovery path;
6. a new exact user approval for that read-only operation.

Read-only discovery would inventory real runtime class, element name, ancestor
path, and match count. It would not authorize styling. A later style preview
would require another gate, a separately launched Explorer process, reviewed
selectors, a complete original-value receipt, and an explicit rollback timer.

## Completion criteria

- [x] Exact separate-window admission policy exists.
- [x] Three-surface selector model rejects zero and duplicate matches.
- [x] Only three reviewed visual properties are allowed.
- [x] All original values are captured before simulated mutation.
- [x] Partial failure restores in reverse order or remains visibly unresolved.
- [x] Twenty-nine deterministic scenarios pass.
- [x] CI and the publication boundary include the offline model.
- [ ] No live XAML connection exists.
- [ ] No selector has been verified against the current live Explorer build.
- [ ] No live styling has been authorized or performed.
