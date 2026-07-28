# Phase 16 — Explorer XAML Read Bridge Review

Status: **REAL INTERFACE REVIEW OBJECT COMPLETE — UNLINKED AND NOT RUN**

## Outcome

Phase 16 replaces the last purely hypothetical read boundary with source that
compiles against the real Windows XAML Diagnostics interfaces:

- `IXamlDiagnostics`;
- `IVisualTreeService2`;
- `IVisualTreeService2::GetPropertyValuesChain`;
- `IVisualTreeService2::GetProperty`;
- `IXamlDiagnostics::GetIInspectableFromHandle`;
- the read accessors for `SolidColorBrush.Color` and `Brush.Opacity`.

The Windows source is compiled only to an object file for review. It is not
linked into the TAP or controller, is not executed, and has no endpoint or
loader. The existing TAP still rejects `SetSite` with `E_ACCESSDENIED`.

## Exact read boundary

Every request is bound to one admitted Phase 13 capability, one exact Explorer
process/window/thread identity, one surface slot, one instance handle, one
selector hash and one property slot. Sequence order is fixed to the nine
surface-major/property-minor observations.

The bridge accepts only `Background`, `Foreground` and `BorderBrush`. It reads
the property chain first and accepts only `BaseValueSourceLocal`. A null local
value is represented directly. A non-null value must resolve to the exact
runtime class `Windows.UI.Xaml.Media.SolidColorBrush`; color and opacity are
projected into the fixed 192-byte Phase 14 snapshot.

Microsoft documents that `GetPropertyValuesChain` returns the properties and
their sources for an element, and that
`GetIInspectableFromHandle` resolves a cached diagnostics handle. Those two
contracts are the basis of this read-only boundary:

- <https://learn.microsoft.com/windows/win32/api/xamlom/nf-xamlom-ivisualtreeservice-getpropertyvalueschain>
- <https://learn.microsoft.com/windows/win32/api/xamlom/nf-xamlom-ixamldiagnostics-getiinspectablefromhandle>

## Ownership and fail-closed behavior

Successful bounded property-chain arrays are released completely. A failed
foreign call that also returns a pointer is treated as an unknown ownership
transition: the bridge does not guess, records uncertainty and blocks the
observation. COM release attempts and completions must match. An exception,
oversized array, incomplete free, release failure, duplicate property, nonlocal
source or unsupported metadata blocks the result.

The portable policy harness covers 56 synthetic foreign-call observations,
including partial outputs, count bounds, source mismatch, null/object
canonicalization and incomplete releases. It never owns a real diagnostics
site and cannot touch Explorer.

## Deliberately absent mutation surface

The review source does not call or expose:

- `InitializeXamlDiagnosticsEx`;
- `CreateInstance`;
- `SetProperty` or `ClearProperty`;
- resource replacement;
- visual-tree child mutation.

No registry, process, Explorer, Windhawk or filesystem mutation is part of this
phase.

## Future single-window read validation gate

Phase 16 does not generate an executable approval command. A future read-only
validation package must be created from fresh host evidence and limited to one
visible `CabinetWClass` window whose exact title is `C:\`, for at most 60
seconds.

Before such a command can be shown, the package must bind:

1. a fresh compatibility report with every check passed;
2. the armed kill-switch path and absent one-shot permit;
3. the exact process, start time, window, thread and visual-tree generation;
4. the reviewed source and fixed-toolchain binary hashes;
5. zero existing XAML Diagnostics consumers;
6. an available recovery terminal;
7. all other experimental modules off;
8. a new explicit approval in the current task.

That future read validation still may not write a property, start Windhawk,
restart Explorer, terminate a process, or change registry/system files.

Phase 16 grants no permission to connect to Explorer or to run the review
object.
