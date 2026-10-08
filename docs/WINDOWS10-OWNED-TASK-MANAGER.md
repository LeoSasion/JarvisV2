# Windows 10 owned Task Manager v1a

## Delivered boundary

`Jarvis.Win10.OwnedTaskManager` supplies real, read-only process telemetry to
the owned Neural Void WPF desktop. The `SYSTEM` taskbar button opens an owned
Task Manager window with a bounded process ledger containing process name,
PID, normalized CPU use, working set and thread count. The surface supports
open, minimize, restore, drag and close inside the preview process.

This is not a clone or replacement for Windows Task Manager. It has no end
task, priority, affinity, launch, service, window-control or process-control
command. It does not request control-capable process access or expose raw
handles, load native code, install a Hook, inject into Explorer or modify the
live Shell. The current evidence remains:

- `readOnlyProcessTelemetry=true`
- `processControlSupported=false`
- `windowControlSupported=false`
- `injectionSupported=false`
- `activationPermitted=false`
- `liveExplorer=not-run`
- `mutationPerformed=false`

## Data path and portability seam

`IProcessCounterSource` is the single input boundary. The Win10-owned
`WindowsProcessSnapshotSource` implements it with managed `Process`
enumeration and reads only the counters needed by the ledger. Expected access
denial and process-exit races skip the affected row instead of failing the
whole sample.

`TaskSnapshotSampler` consumes that interface, establishes a first-frame CPU
baseline, rejects PID reuse through process start time, normalizes CPU deltas
by processor count, applies deterministic ordering and limits output to at
most 256 rows. The WPF surface consumes only the resulting snapshot contract.

The injectable source keeps operating-system sampling out of the UI logic and
supports deterministic tests. A future independently owned GNOME, Plasma or
wlroots backend can provide its own platform source against the same snapshot
shape (or promote that shape to `src/common`) without importing WPF or the
Windows `Process` adapter. This seam is preparation for another backend, not a
Linux support claim.

## Sampling lifecycle

Sampling starts only when the owned window is opened or restored. Minimize,
close, preview-host unload and host-window minimize stop the refresh timer.
Generation checks discard a late in-flight result after sampling stops. A
transient read failure keeps the previous snapshot visible and labels the
refresh as delayed.

Stop/start resets the CPU baseline, so the first resumed frame reports
`LEARNING`. A restart while an earlier capture is in flight queues one immediate
refresh after that capture retires; old-generation results cannot publish.
Snapshot publication is explicitly dispatched to the WPF UI thread. Rows retain
their identity and selection while PID, name and process start time remain the
same; a restarted process receives a new row. Pausing after a read fault removes
the retrying label.

The window participates in [owned-window layouts](WINDOWS10-OWNED-WINDOW-LAYOUT.md)
and preserves a usable process ledger in compact panes.

The deterministic render path uses fixed preview rows and does not enumerate
live processes. Its receipt identifies `surfaceState=owned-task-manager-preview`
and preserves the same read-only/no-control boundary.

## Verification

Run the independent model, external contract and real read-only snapshot gate:

```powershell
pwsh -File .\scripts\Test-Windows10OwnedTaskManager.ps1
```

The Neural Void gate additionally checks the owned WPF surface, its explicit
read-only labeling and its open/minimize/restore/close sampling lifecycle:

```powershell
pwsh -File .\scripts\Test-Windows10NeuralVoidPreview.ps1
```

Neither command authorizes or performs Explorer injection. Any future Shell
collector remains a separate Win10-native workstream with its own automated
preflight, exact-target admission and recovery receipt.
