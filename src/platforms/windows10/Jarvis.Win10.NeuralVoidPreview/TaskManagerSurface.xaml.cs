using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Jarvis.Win10.OwnedTaskManager;

namespace Jarvis.Win10.NeuralVoidPreview;

public sealed class TaskManagerProcessRow : INotifyPropertyChanged
{
    private string _processName = string.Empty;
    private string _processIdText = string.Empty;
    private string _cpuPercentText = string.Empty;
    private string _workingSetText = string.Empty;
    private string _threadCountText = string.Empty;
    private string _automationName = string.Empty;

    internal TaskManagerProcessRow(int processId, string identityName, long startTimeUtcTicks)
    {
        ProcessId = processId;
        IdentityName = identityName;
        StartTimeUtcTicks = startTimeUtcTicks;
    }

    internal int ProcessId { get; }

    internal string IdentityName { get; }

    internal long StartTimeUtcTicks { get; }

    public string ProcessName
    {
        get => _processName;
        private set => SetField(ref _processName, value);
    }

    public string ProcessIdText
    {
        get => _processIdText;
        private set => SetField(ref _processIdText, value);
    }

    public string CpuPercentText
    {
        get => _cpuPercentText;
        private set => SetField(ref _cpuPercentText, value);
    }

    public string WorkingSetText
    {
        get => _workingSetText;
        private set => SetField(ref _workingSetText, value);
    }

    public string ThreadCountText
    {
        get => _threadCountText;
        private set => SetField(ref _threadCountText, value);
    }

    public string AutomationName
    {
        get => _automationName;
        private set => SetField(ref _automationName, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Update(
        string processName,
        string processIdText,
        string cpuPercentText,
        string workingSetText,
        string threadCountText,
        string automationName)
    {
        ProcessName = processName;
        ProcessIdText = processIdText;
        CpuPercentText = cpuPercentText;
        WorkingSetText = workingSetText;
        ThreadCountText = threadCountText;
        AutomationName = automationName;
    }

    private void SetField(
        ref string field,
        string value,
        [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

public partial class TaskManagerSurface : UserControl
{
    private static readonly TimeSpan DefaultRefreshInterval =
        TimeSpan.FromSeconds(1);

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(
            nameof(AccentBrush),
            typeof(Brush),
            typeof(TaskManagerSurface),
            new FrameworkPropertyMetadata(Brushes.Yellow));

    private readonly TaskSnapshotSampler _sampler;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DataTemplate _fullProcessRowTemplate;
    private readonly DataTemplate _compactProcessRowTemplate;
    private int _samplingGeneration;
    private bool _refreshInFlight;
    private bool _refreshPending;
    private bool _resetBaselineBeforeNextCapture = true;
    private bool _compactWidth;
    private bool _compactHeight;

    public TaskManagerSurface()
        : this(
            new TaskSnapshotSampler(new WindowsProcessSnapshotSource()),
            DefaultRefreshInterval)
    {
    }

    internal TaskManagerSurface(
        TaskSnapshotSampler sampler,
        TimeSpan refreshInterval)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        if (refreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshInterval));
        }

        ProcessRows = new ObservableCollection<TaskManagerProcessRow>();
        _sampler = sampler;
        InitializeComponent();
        _fullProcessRowTemplate = ProcessList.ItemTemplate;
        _compactProcessRowTemplate =
            (DataTemplate)FindResource("CompactProcessRowTemplate");
        _refreshTimer = new DispatcherTimer(
            refreshInterval,
            DispatcherPriority.Background,
            RefreshTimer_OnTick,
            Dispatcher)
        {
            IsEnabled = false,
        };
        SetSamplingState(SamplingPresentationState.Paused);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public ObservableCollection<TaskManagerProcessRow> ProcessRows { get; }

    internal bool IsSamplingActiveForTest => _refreshTimer.IsEnabled;

    internal string SamplingStateForTest => SamplingStateText.Text;

    internal string CpuSummaryForTest => CpuSummaryText.Text;

    internal string MemorySummaryForTest => MemorySummaryText.Text;

    internal string ReadStatusForTest => ReadStatusText.Text;

    internal bool IsCompactWidthForTest => _compactWidth;

    internal bool IsCompactHeightForTest => _compactHeight;

    internal double ProcessViewportHeightForTest => ProcessList.ActualHeight;

    internal bool ProcessListHasKeyboardFocusForTest =>
        ProcessList.IsKeyboardFocusWithin;

    internal string SelectedProcessName =>
        (ProcessList.SelectedItem as TaskManagerProcessRow)?.ProcessName ??
            string.Empty;

    /// <summary>
    /// Moves the read-only process selection by a relative offset and scrolls
    /// it into view. Returns false when the target index is out of range, so
    /// callers can keep the announcement stable at the edges.
    /// </summary>
    internal bool TryMoveProcessSelection(int delta) =>
        MoveProcessSelection(ProcessList.SelectedIndex + delta);

    /// <summary>
    /// Selects the first or last process row and scrolls it into view.
    /// Returns false when the ledger is empty.
    /// </summary>
    internal bool TrySelectEdgeProcess(bool first) =>
        ProcessRows.Count == 0
            ? false
            : MoveProcessSelection(first ? 0 : ProcessRows.Count - 1);

    private bool MoveProcessSelection(int index)
    {
        if (index < 0 || index >= ProcessRows.Count)
        {
            return false;
        }

        TaskManagerProcessRow row = ProcessRows[index];
        ProcessList.SelectedItem = row;
        ProcessList.ScrollIntoView(row);
        return true;
    }

    internal void SetLayoutDensity(bool compactWidth, bool compactHeight)
    {
        _compactWidth = compactWidth;
        _compactHeight = compactHeight;
        bool compact = compactWidth || compactHeight;
        SummaryPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        StatusPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactSamplingStateText.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        SummaryRow.Height = compact
            ? new GridLength(0.0)
            : new GridLength(126.0);
        StatusRow.Height = compact
            ? new GridLength(0.0)
            : new GridLength(40.0);
        FullProcessHeader.Visibility = compactWidth
            ? Visibility.Collapsed
            : Visibility.Visible;
        CompactProcessHeader.Visibility = compactWidth
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProcessList.ItemTemplate = compactWidth
            ? _compactProcessRowTemplate
            : _fullProcessRowTemplate;
        AutomationProperties.SetHelpText(
            ProcessList,
            compact
                ? "Compact read-only process ledger; hidden summary fields remain available in row automation names."
                : "Full read-only process ledger.");
    }

    public void StartSampling()
    {
        if (_refreshTimer.IsEnabled)
        {
            return;
        }

        _samplingGeneration++;
        _refreshTimer.Start();
        SetSamplingState(SamplingPresentationState.Sampling);
        RequestRefresh(_samplingGeneration);
    }

    public void StopSampling()
    {
        _samplingGeneration++;
        _refreshTimer.Stop();
        _refreshPending = false;
        _resetBaselineBeforeNextCapture = true;
        SetSamplingState(SamplingPresentationState.Paused);
        const string retrySuffix = " / RETRYING";
        if (ReadStatusText.Text.EndsWith(retrySuffix, StringComparison.Ordinal))
        {
            ReadStatusText.Text = ReadStatusText.Text[..^retrySuffix.Length];
            AutomationProperties.SetName(ReadStatusText, ReadStatusText.Text);
        }
    }

    internal void StartSamplingWithoutRefreshForTest()
    {
        if (_refreshTimer.IsEnabled)
        {
            return;
        }

        _samplingGeneration++;
        _refreshTimer.Start();
        SetSamplingState(SamplingPresentationState.Sampling);
    }

    internal void RefreshSynchronouslyForTest()
    {
        try
        {
            ResetBaselineIfRequired();
            OwnedTaskSnapshot snapshot = _sampler.Capture();
            ApplySnapshot(snapshot, false);
        }
        catch (Exception exception)
        {
            ApplyReadFault(exception);
        }
    }

    internal void ApplySnapshotForPreview(OwnedTaskSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ApplySnapshot(snapshot, true);
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs eventArgs) =>
        RequestRefresh(_samplingGeneration);

    private void RequestRefresh(int generation)
    {
        if (generation != _samplingGeneration ||
            !_refreshTimer.IsEnabled)
        {
            return;
        }

        if (_refreshInFlight)
        {
            _refreshPending = true;
            return;
        }

        ResetBaselineIfRequired();
        _refreshInFlight = true;
        _ = CaptureAndDispatchAsync(generation);
    }

    private async Task CaptureAndDispatchAsync(int generation)
    {
        OwnedTaskSnapshot? snapshot = null;
        Exception? fault = null;
        try
        {
            snapshot = await Task.Run(_sampler.Capture).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            fault = exception;
        }

        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (generation == _samplingGeneration && _refreshTimer.IsEnabled)
                    {
                        if (fault is not null)
                        {
                            ApplyReadFault(fault);
                        }
                        else if (snapshot is not null)
                        {
                            ApplySnapshot(snapshot, false);
                        }
                    }
                }
                finally
                {
                    _refreshInFlight = false;
                    if (_refreshPending && _refreshTimer.IsEnabled)
                    {
                        _refreshPending = false;
                        RequestRefresh(_samplingGeneration);
                    }
                }
            }, DispatcherPriority.Background).Task.ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted)
        {
            // The host is closing; no new capture or UI publication is admitted.
        }
    }

    private void ApplySnapshot(
        OwnedTaskSnapshot snapshot,
        bool deterministicPreview)
    {
        ReconcileProcessRows(snapshot.Processes);

        double totalCpu = Math.Clamp(
            snapshot.Processes
                .Where(process => process.CpuPercentMeasured)
                .Sum(process => process.CpuPercent),
            0.0,
            100.0);
        bool hasMeasuredCpu =
            snapshot.Processes.Any(process => process.CpuPercentMeasured);
        long visibleWorkingSet = snapshot.Processes.Aggregate(
            0L,
            (total, process) =>
                process.WorkingSetBytes > long.MaxValue - total
                    ? long.MaxValue
                    : total + process.WorkingSetBytes);

        CpuSummaryText.Text = hasMeasuredCpu
            ? $"{totalCpu.ToString("0.0", CultureInfo.InvariantCulture)} %"
            : "LEARNING";
        MemorySummaryText.Text = FormatBytes(visibleWorkingSet);
        ProcessCountText.Text =
            $"{snapshot.ProcessCount} VISIBLE / " +
            $"{snapshot.EnumeratedProcessCount} ENUMERATED";
        CapturedAtText.Text =
            $"SAMPLE {snapshot.CapturedAtUtc:HH:mm:ss} UTC";
        ReadStatusText.Text =
            snapshot.SkippedProcessCount == 0
                ? "ALL ENUMERATED PROCESSES READ"
                : $"{snapshot.SkippedProcessCount} TRANSIENT OR ACCESS-LIMITED " +
                    "PROCESSES SKIPPED";
        AutomationProperties.SetName(ReadStatusText, ReadStatusText.Text);
        EmptyStatePanel.Visibility = ProcessRows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (deterministicPreview)
        {
            SetSamplingState(SamplingPresentationState.DeterministicPreview);
        }
        else if (_refreshTimer.IsEnabled)
        {
            SetSamplingState(SamplingPresentationState.Sampling);
        }

        AutomationProperties.SetName(
            ProcessList,
            $"Read-only process telemetry, {ProcessRows.Count} visible rows");
        AutomationProperties.SetName(
            CpuSummaryText,
            $"Visible process CPU {CpuSummaryText.Text}");
        AutomationProperties.SetName(
            MemorySummaryText,
            $"Visible process working set {MemorySummaryText.Text}");
    }

    private void ApplyReadFault(Exception exception)
    {
        SetSamplingState(
            _refreshTimer.IsEnabled
                ? SamplingPresentationState.DelayedRetrying
                : SamplingPresentationState.Paused);
        ReadStatusText.Text =
            $"REFRESH RETAINED / {exception.GetType().Name.ToUpperInvariant()}" +
            (_refreshTimer.IsEnabled ? " / RETRYING" : string.Empty);
        AutomationProperties.SetName(
            ReadStatusText,
            "Process refresh delayed; previous read-only snapshot retained");
    }

    private void ResetBaselineIfRequired()
    {
        if (!_resetBaselineBeforeNextCapture)
        {
            return;
        }

        _sampler.ResetBaseline();
        _resetBaselineBeforeNextCapture = false;
    }

    private void ReconcileProcessRows(
        IReadOnlyList<OwnedTaskProcessSnapshot> processes)
    {
        TaskManagerProcessRow? selected =
            ProcessList.SelectedItem as TaskManagerProcessRow;
        Dictionary<(int ProcessId, string ProcessName, long StartTimeUtcTicks), TaskManagerProcessRow>
            existing = ProcessRows.ToDictionary(
                row => (row.ProcessId, row.IdentityName, row.StartTimeUtcTicks));
        List<TaskManagerProcessRow> desired = new(processes.Count);
        foreach (OwnedTaskProcessSnapshot process in processes)
        {
            (int ProcessId, string ProcessName, long StartTimeUtcTicks) key =
                (process.ProcessId, process.ProcessName, process.StartTimeUtcTicks);
            if (!existing.Remove(key, out TaskManagerProcessRow? row))
            {
                row = new TaskManagerProcessRow(
                    process.ProcessId,
                    process.ProcessName,
                    process.StartTimeUtcTicks);
            }

            UpdateProcessRow(row, process);
            desired.Add(row);
        }

        for (int index = 0; index < desired.Count; index++)
        {
            TaskManagerProcessRow row = desired[index];
            int currentIndex = ProcessRows.IndexOf(row);
            if (currentIndex < 0)
            {
                ProcessRows.Insert(index, row);
            }
            else if (currentIndex != index)
            {
                ProcessRows.Move(currentIndex, index);
            }
        }

        while (ProcessRows.Count > desired.Count)
        {
            ProcessRows.RemoveAt(ProcessRows.Count - 1);
        }

        if (selected is not null && ProcessRows.Contains(selected))
        {
            ProcessList.SelectedItem = selected;
        }
    }

    private static void UpdateProcessRow(
        TaskManagerProcessRow row,
        OwnedTaskProcessSnapshot process)
    {
        string cpu = process.CpuPercentMeasured
            ? $"{process.CpuPercent.ToString("0.00", CultureInfo.InvariantCulture)} %"
            : "LEARNING";
        string memory = FormatBytes(process.WorkingSetBytes);
        row.Update(
            process.ProcessName.ToUpperInvariant(),
            process.ProcessId.ToString(CultureInfo.InvariantCulture),
            cpu,
            memory,
            process.ThreadCount.ToString(CultureInfo.InvariantCulture),
            $"{process.ProcessName}, PID {process.ProcessId}, CPU {cpu}, " +
                $"memory {memory}, {process.ThreadCount} threads");
    }

    private void SetSamplingState(SamplingPresentationState state)
    {
        (string text, string automationName, bool active) = state switch
        {
            SamplingPresentationState.Sampling =>
                ("SAMPLING", "Task Manager state: sampling", true),
            SamplingPresentationState.DelayedRetrying =>
                ("READ DELAYED / RETRYING",
                    "Task Manager state: read delayed; retrying", true),
            SamplingPresentationState.DeterministicPreview =>
                ("DETERMINISTIC PREVIEW",
                    "Task Manager state: deterministic preview", true),
            _ => ("PAUSED", "Task Manager state: paused", false),
        };
        SamplingStateText.Text = text;
        SamplingStateMarker.Fill = active
            ? AccentBrush
            : (Brush)FindResource("SecondaryTextBrush");
        AutomationProperties.SetName(
            SamplingStateText,
            automationName);
    }

    internal static string FormatBytes(long byteCount)
    {
        double value = Math.Max(byteCount, 0);
        string unit = "B";
        if (value >= 1024.0 * 1024.0 * 1024.0)
        {
            value /= 1024.0 * 1024.0 * 1024.0;
            unit = "GB";
        }
        else if (value >= 1024.0 * 1024.0)
        {
            value /= 1024.0 * 1024.0;
            unit = "MB";
        }
        else if (value >= 1024.0)
        {
            value /= 1024.0;
            unit = "KB";
        }

        string format = unit == "B" ? "0" : "0.0";
        return $"{value.ToString(format, CultureInfo.InvariantCulture)} {unit}";
    }

    private enum SamplingPresentationState
    {
        Paused,
        Sampling,
        DelayedRetrying,
        DeterministicPreview,
    }
}
