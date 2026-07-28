# Phase 17 — Explorer XAML Surface Discovery Review

Status: **BOUNDED DISCOVERY CORE COMPLETE — CALLBACK UNLINKED AND NOT RUN**

## Outcome

Phase 17 closes the offline gap between a stream of XAML visual-tree events and
the nine exact Phase 16 property-read requests. It adds:

- a fixed-capacity visual-tree discovery core;
- exact matching for the tab strip, command bar and navigation pane;
- a real `IVisualTreeServiceCallback2` review object that compiles but is not
  linked or executed;
- a read-only host review-package generator that deliberately refuses to
  generate a connection command.

This is still not live XAML evidence. The shipping TAP continues to reject
`SetSite` with `E_ACCESSDENIED`.

## Exact three-surface contract

The discovery core accepts only the three hashed selectors already approved as
offline candidates:

1. `FileExplorerExtensions.FileExplorerTabControl > * > Microsoft.UI.Xaml.Controls.Grid#TabContainerGrid`;
2. `FileExplorerExtensions.CommandBarControl > * > Microsoft.UI.Xaml.Controls.Grid#CommandBarControlRootGrid`;
3. `Microsoft.UI.Xaml.Controls.NavigationView`.

The wildcard matches zero or more ancestors, following the GPL-3.0 upstream
selector semantics already recorded in the repository. Each surface must match
exactly once, and the three instance handles must be distinct. Missing,
duplicate or colliding matches block the entire session.

## Bounded fail-closed model

The portable core has fixed limits of 512 unique handles, 2,048 events and 64
ancestor levels. It performs no heap allocation. Sequence gaps, handle replay,
unknown removes, orphans, cycles and over-depth trees become terminal blocked
states. A removed handle cannot be reused in the same one-shot generation.

After a unique finalization, the core emits exactly nine
`jarvis_tap_xaml_read_request` records in surface-major/property-minor order.
Those requests carry the discovered instance handle and the exact selector
hash expected by the Phase 16 bridge. The discovery core itself never reads a
property.

The portable harness covers 58 synthetic scenarios. No harness scenario owns a
Windows diagnostics site, subscribes a callback, loads the TAP or touches
Explorer.

## Real callback review object

`jarvis_explorer_tap_surface_discovery_windows.cpp` compiles against
`IVisualTreeServiceCallback2`. It only classifies:

- `ParentChildRelation.Parent`, `Child` and `ChildIndex`;
- `VisualElement.Handle`, `Type` and `Name`;
- add and remove notifications.

The object contains no call to `InitializeXamlDiagnosticsEx`,
`AdviseVisualTreeChange`, `UnadviseVisualTreeChange`,
`GetIInspectableFromHandle`, any property getter or any property setter. The
review object is compiled separately and is not linked into the TAP or
controller.

## Fresh host review package

`New-ExplorerXamlReadReviewPackage.ps1` performs only read-only checks:

- fresh Supervisor compatibility inspection;
- armed kill switch and absent one-shot permit;
- Windhawk Stopped / Manual / PID 0;
- Explorer Windhawk/JARVIS module baseline;
- hashes of the exact Phase 17 sources and contract.

The package is always blocked for connection in this phase. It records the
remaining terminal blockers: no exact `C:\` window binding, no visual-tree
generation, no existing-consumer inspection, no plan-bound recovery terminal,
an unlinked callback and a describe-only controller. It generates no exact
command.

## Deliberately absent live behavior

Phase 17 does not:

- connect XAML Diagnostics;
- load a TAP DLL;
- subscribe to the visual tree;
- read or write a live XAML property;
- start Windhawk;
- restart or terminate Explorer;
- change the registry or system files.

A later phase must review a connectable single-window read controller and
produce a new fresh package. Only then may the exact 60-second read command be
shown for a new explicit approval in the current task.
