using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static Jarvis.Win10.NeuralVoidPreview.VisualTreeSearch;

namespace Jarvis.Win10.NeuralVoidPreview;

/// <summary>One independently mutable, memory-only Explorer session.</summary>
public partial class ExplorerSurface : UserControl
{
    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush),
            typeof(ExplorerSurface), new FrameworkPropertyMetadata(Brushes.Yellow));
    private readonly ExplorerWorkspace _explorerWorkspace = new();
    private readonly DataTemplate _explorerFullItemTemplate;
    private readonly DataTemplate _explorerCompactItemTemplate;
    private bool _refreshingExplorerPresentation;
    private DispatcherOperation? _pendingExplorerFocus;
    private int _focusIntentGeneration;
    private bool _interactionEnabled = true;

    public ExplorerSurface()
    {
        InitializeComponent();
        _explorerFullItemTemplate = ExplorerItemList.ItemTemplate;
        _explorerCompactItemTemplate = (DataTemplate)FindResource("ExplorerCompactItemTemplate");
        _explorerWorkspace.Changed += ExplorerWorkspace_OnChanged;
        Loaded += (_, _) => ResumeInteraction();
        Unloaded += (_, _) => SuspendInteraction();
        RefreshExplorerPresentation();
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    internal ExplorerWorkspace Workspace => _explorerWorkspace;
    internal event Action<string>? Announced;
    internal event Action? ActivationRequested;
    internal Func<bool>? IsFocusIntentCurrent { get; set; }

    internal void CancelPendingFocus()
    {
        _focusIntentGeneration++;
        if (_pendingExplorerFocus is { Status: DispatcherOperationStatus.Pending } pending) pending.Abort();
        _pendingExplorerFocus = null;
    }

    internal void SuspendInteraction()
    {
        _interactionEnabled = false;
        CancelPendingFocus();
        if (Mouse.Captured is DependencyObject captured &&
            (ReferenceEquals(captured, this) || IsAncestorOf(captured))) Mouse.Capture(null);
    }

    internal void ResumeInteraction() => _interactionEnabled = true;

    private bool RequestInteraction()
    {
        if (!_interactionEnabled || !IsLoaded || !IsVisible) return false;
        ActivationRequested?.Invoke();
        return true;
    }

    private void QueueExplorerFocus(Action action)
    {
        CancelPendingFocus();
        int generation = _focusIntentGeneration;
        IInputElement? focusOrigin = Keyboard.FocusedElement;
        _pendingExplorerFocus = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            _pendingExplorerFocus = null;
            if (generation == _focusIntentGeneration && _interactionEnabled && IsLoaded && IsVisible &&
                ReferenceEquals(Keyboard.FocusedElement, focusOrigin) &&
                (IsFocusIntentCurrent?.Invoke() ?? true)) action();
        }));
    }
    internal bool IsCompactWidth { get; private set; }
    internal bool IsCompactHeight { get; private set; }
    internal void SetLayoutDensity(bool compactWidth, bool compactHeight, double availableWidth)
    {
        IsCompactWidth = compactWidth;
        IsCompactHeight = compactHeight;
        bool compact = IsCompactWidth || IsCompactHeight;
        // A full-size clip changes ClearType compositing; keep the initial
        // composition untouched and constrain only admitted compact panes.
        ClipToBounds = compact;
        ExplorerListHeaderRow.Height = new GridLength(compactHeight ? 24 : compact ? 40 : 62);
        ExplorerNavigationRow.Height = new GridLength(compactHeight ? 32 : 44);
        foreach (Button navigation in new[] { ExplorerBackButton, ExplorerForwardButton, ExplorerUpButton })
            navigation.Height = compactHeight ? 28 : 34;
        ExplorerPreviewHeaderRow.Height = new GridLength(compactHeight ? 28 : compact ? 32 : 62);
        ExplorerPreviewCloseButton.Height = compact ? 28 : 40;
        ExplorerPreviewCloseButton.Width = compact ? 28 : 44;
        ExplorerPreviewFullContent.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ExplorerCompactPreviewContent.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        if (ExplorerActionPanel.Child is StackPanel actions)
        {
            actions.Margin = new Thickness(compact ? 8 : 20, 0, 0, 0);
            foreach (Button button in actions.Children.OfType<Button>())
            {
                button.Width = compact ? Math.Max(28, Math.Min(116, (availableWidth - 16) / 4)) : 116;
                button.Height = compactHeight ? 30 : compact ? 46 : 88;
                if (button.Content is StackPanel content)
                {
                    if (content.Children[0] is Viewbox icon) icon.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                    if (content.Children[1] is TextBlock label) label.Margin = new Thickness(0, compact ? 0 : 7, 0, 0);
                }
            }
        }
        ExplorerFolderPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ExplorerActionPanel.Visibility = Visibility.Visible;
        ExplorerStoragePanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ExplorerFolderColumn.Width = compact
            ? new GridLength(0.0)
            : new GridLength(286.0);
        ExplorerFolderDividerColumn.Width = compact
            ? new GridLength(0.0)
            : new GridLength(1.0);
        ExplorerFullHeader.Visibility = IsCompactWidth
            ? Visibility.Collapsed
            : Visibility.Visible;
        ExplorerCompactHeader.Visibility = IsCompactWidth
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyExplorerDensityTemplate(IsCompactWidth ? _explorerCompactItemTemplate : _explorerFullItemTemplate);
        ExplorerActionRow.Height = new GridLength(compactHeight ? 32 : compact ? 48 : 90);
        ExplorerStorageRow.Height = compact
            ? new GridLength(0.0)
            : new GridLength(101.0);
        UpdateCompactRenameError();
    }

    private TextBox? CurrentRenameEditor()
    {
        if (Workspace.RenameItemId is not string id || ExplorerItemList.SelectedItem is not ExplorerWorkspaceItem item || item.Id != id ||
            ExplorerItemList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) return null;
        return FindVisualDescendant<TextBox>(container) is TextBox editor && editor.IsVisible && IsAncestorOf(editor) ? editor : null;
    }

    private void ApplyExplorerDensityTemplate(DataTemplate template)
    {
        if (ReferenceEquals(ExplorerItemList.ItemTemplate,template)) return;
        TextBox? departing = CurrentRenameEditor();
        if (departing is null) { ExplorerItemList.ItemTemplate = template; return; }
        string? renameId = Workspace.RenameItemId;
        Panel? departingParent = departing.Parent as Panel;
        // Keep the physical TextEditor/selection, including its anchor and moving
        // end. Freeze template-owned values before teardown; Select(start,length)
        // on a replacement would silently normalize a backward selection.
        departing.Text = departing.Text;
        departing.Style = departing.Style;
        bool ownedFocus = ReferenceEquals(Keyboard.FocusedElement,departing);
        bool wasRefreshing = _refreshingExplorerPresentation;
        _refreshingExplorerPresentation = true;
        try
        {
            // Replace only the density template while blur is guarded. Materialize
            // synchronously so no deferred restore can steal a newer focus intent.
            ExplorerItemList.ItemTemplate = template;
            ExplorerItemList.UpdateLayout();
            // A focused row can survive outside the realized viewport after a
            // density round-trip. Once unfocused, template refresh drops it;
            // realize that same rename target before discarding its local draft.
            if (Workspace.RenameItemId == renameId && CurrentRenameEditor() is null &&
                ExplorerItemList.SelectedItem is ExplorerWorkspaceItem selected && selected.Id == renameId)
            {
                ExplorerItemList.ScrollIntoView(selected);
                ExplorerItemList.UpdateLayout();
            }
            if (Workspace.RenameItemId != renameId || CurrentRenameEditor() is not TextBox replacement ||
                departingParent is null || replacement.Parent is not Panel replacementParent) return;
            int slot = replacementParent.Children.IndexOf(replacement);
            departingParent.Children.Remove(departing);
            replacementParent.Children.RemoveAt(slot);
            replacementParent.Children.Insert(slot, departing);
            AutomationProperties.SetName(departing, AutomationProperties.GetName(replacement));
            departing.LostKeyboardFocus -= ExplorerRenameEditor_OnLostKeyboardFocus;
            departing.LostKeyboardFocus += ExplorerRenameEditor_OnLostKeyboardFocus;
            if (ownedFocus && _interactionEnabled && IsLoaded && IsVisible && (IsFocusIntentCurrent?.Invoke() ?? true) &&
                (Keyboard.FocusedElement is null || ReferenceEquals(Keyboard.FocusedElement,departing) ||
                    ReferenceEquals(Keyboard.FocusedElement,ExplorerItemList)))
                _ = departing.Focus();
        }
        finally { _refreshingExplorerPresentation = wasRefreshing; }
    }

    private void Announce(string text) => Announced?.Invoke(text);

    internal void NavigateTo(string locationId)
    {
        if (!RequestInteraction()) return;
        ClearExplorerPreview();
        _ = Workspace.TryNavigateTo(locationId);
        AnnounceExplorerLocation();
        FocusExplorerItems();
    }

    private void ExplorerBackButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        NavigateExplorerHistory(_explorerWorkspace.TryGoBack);

    private void ExplorerForwardButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        NavigateExplorerHistory(_explorerWorkspace.TryGoForward);

    private void ExplorerUpButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        NavigateExplorerHistory(_explorerWorkspace.TryGoUp);

    private void ExplorerOpenButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        ApplyExplorerOpenResult(_explorerWorkspace.OpenSelected());

    private void ExplorerRenameButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        BeginExplorerRename();

    private void ExplorerAccessButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        Announce("EXPLORER / ACCESS PANEL NOT AVAILABLE IN THIS PREVIEW");

    private void ExplorerNewFolderButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        CreateExplorerFolder();

    private void ExplorerWorkspace_OnChanged(object? sender, EventArgs eventArgs)
    {
        CancelPendingFocus();
        RefreshExplorerPresentation();
    }

    private void RefreshExplorerPresentation()
    {
        _refreshingExplorerPresentation = true;
        try
        {
            ExplorerWorkspaceSnapshot snapshot =
                _explorerWorkspace.CaptureSnapshot();
            string breadcrumb = string.Join(
                "  /  ",
                snapshot.Breadcrumb.Select(segment =>
                    segment.Name.ToUpperInvariant()));

            ExplorerBreadcrumbText.Text = breadcrumb;
            ExplorerBreadcrumbText.ToolTip = breadcrumb;
            AutomationProperties.SetName(
                ExplorerBreadcrumbText,
                $"Current Explorer location: {breadcrumb}");

            string folderName =
                snapshot.CurrentLocation.Name.ToUpperInvariant();
            ExplorerFolderNameText.Text = folderName;
            ExplorerFolderNameText.ToolTip = folderName;
            AutomationProperties.SetName(
                ExplorerFolderNameText,
                $"Current folder: {folderName}");

            int itemCount = snapshot.Items.Count;
            string itemCountText = itemCount == 1
                ? "1 ITEM"
                : $"{itemCount} ITEMS";
            ExplorerItemCountText.Text = itemCountText;
            AutomationProperties.SetName(
                ExplorerItemCountText,
                itemCountText);

            ExplorerItemList.ItemsSource = snapshot.Items;
            ExplorerWorkspaceItem? selected = snapshot.Items.FirstOrDefault(
                item => item.IsSelected);
            ExplorerItemList.SelectedItem = selected;

            ExplorerBackButton.IsEnabled = snapshot.CanGoBack;
            ExplorerForwardButton.IsEnabled = snapshot.CanGoForward;
            ExplorerUpButton.IsEnabled = snapshot.CanGoUp;
            ExplorerOpenButton.IsEnabled = selected is not null;
            ExplorerRenameButton.IsEnabled = selected is not null;
            ExplorerAccessButton.IsEnabled = false;
            ExplorerNewFolderButton.IsEnabled = true;
            ExplorerEmptyState.Visibility = snapshot.IsEmpty
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (snapshot.RenameItemId is null)
            {
                ClearExplorerRenameError();
            }
        }
        finally
        {
            _refreshingExplorerPresentation = false;
        }
    }

    private void ClearExplorerRenameError()
    {
        ExplorerRenameErrorText.Text = string.Empty;
        ExplorerRenameErrorText.Visibility = Visibility.Collapsed;
        UpdateCompactRenameError();
        AutomationProperties.SetName(
            ExplorerRenameErrorText,
            "No Explorer rename error");
    }

    private void ShowExplorerRenameError(string message)
    {
        ExplorerRenameErrorText.Text = message.ToUpperInvariant();
        ExplorerRenameErrorText.Visibility = Visibility.Visible;
        UpdateCompactRenameError();
        AutomationProperties.SetName(
            ExplorerRenameErrorText,
            $"Explorer rename error: {message}");
    }

    private void UpdateCompactRenameError()
    {
        bool show = (IsCompactWidth || IsCompactHeight) && !string.IsNullOrEmpty(ExplorerRenameErrorText.Text);
        ExplorerCompactRenameErrorText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ExplorerBreadcrumbText.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void NavigateExplorerHistory(Func<bool> navigation)
    {
        if (!RequestInteraction()) return;
        ClearExplorerPreview();
        ClearExplorerRenameError();
        if (navigation())
        {
            AnnounceExplorerLocation();
        }
        else
        {
            Announce("EXPLORER / NAVIGATION BOUNDARY");
        }

        FocusExplorerItems();
    }

    private void AnnounceExplorerLocation()
    {
        string location = string.Join(
            " / ",
            _explorerWorkspace.Breadcrumb.Select(segment => segment.Name));
        Announce($"EXPLORER / {location}");
    }

    internal void FocusExplorerItems()
    {
        if (ExplorerPreviewPanel.Visibility == Visibility.Visible)
        {
            QueueExplorerFocus(() => _ = ExplorerPreviewCloseButton.Focus());
        }
        else if (Workspace.RenameItemId is not null)
        {
            QueueExplorerRenameFocus();
        }
        else
        {
            QueueExplorerFocus(() => _ = ExplorerItemList.Focus());
        }
    }

    internal void ApplyExplorerOpenResult(ExplorerOpenResult result)
    {
        if (!RequestInteraction()) return;
        ClearExplorerRenameError();
        switch (result.Disposition)
        {
            case ExplorerOpenDisposition.Navigated:
                ClearExplorerPreview();
                AnnounceExplorerLocation();
                FocusExplorerItems();
                break;
            case ExplorerOpenDisposition.PreviewRequested:
                ExplorerPreviewRequest request = result.PreviewRequest ??
                    throw new InvalidOperationException(
                        "explorer-preview-request-missing");
                ShowExplorerPreview(request);
                Announce(
                    $"EXPLORER / OWNED PREVIEW / {request.DisplayName}");
                break;
            default:
                ClearExplorerPreview();
                Announce("EXPLORER / SELECT AN ITEM");
                FocusExplorerItems();
                break;
        }
    }

    private void BeginExplorerRename()
    {
        if (!RequestInteraction()) return;
        ClearExplorerPreview();
        ExplorerRenameResult result =
            _explorerWorkspace.BeginRenameSelected();
        if (!result.Succeeded)
        {
            ShowExplorerRenameError(result.Message);
            Announce("EXPLORER / RENAME UNAVAILABLE");
            return;
        }

        ClearExplorerRenameError();
        Announce($"EXPLORER / RENAME / {result.Name}");
        QueueExplorerRenameFocus();
    }

    private void CreateExplorerFolder()
    {
        if (!RequestInteraction()) return;
        ClearExplorerPreview();
        ExplorerWorkspaceItem folder =
            _explorerWorkspace.CreateUniqueFolder();
        ClearExplorerRenameError();
        Announce($"EXPLORER / NEW FOLDER / {folder.Name}");
        QueueExplorerRenameFocus();
    }

    private void QueueExplorerRenameFocus()
    {
        string? renameId = Workspace.RenameItemId;
        QueueExplorerFocus(() =>
        {
            if (renameId is null || Workspace.RenameItemId != renameId ||
                ExplorerItemList.SelectedItem is not ExplorerWorkspaceItem selected || selected.Id != renameId) return;
            ExplorerItemList.ScrollIntoView(selected);
            ExplorerItemList.UpdateLayout();
            if (ExplorerItemList.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem container &&
                FindVisualDescendant<TextBox>(container) is TextBox { IsVisible: true } editor && IsAncestorOf(editor))
            {
                _ = editor.Focus();
                editor.SelectAll();
            }
        });
    }

    private void CommitExplorerRename(TextBox editor, bool explicitSubmission = true)
    {
        if (!_interactionEnabled || !IsLoaded || !IsVisible || !IsAncestorOf(editor) ||
            editor.DataContext is not ExplorerWorkspaceItem item ||
            !StringComparer.Ordinal.Equals(
                item.Id,
                _explorerWorkspace.RenameItemId))
        {
            return;
        }

        // Blur validates the departing editor but is not an activation/focus request.
        // WPF already exposes the new focus here; it must not become our origin.
        if (explicitSubmission)
        {
            if (!RequestInteraction()) return;
        }
        else
        {
            CancelPendingFocus();
        }
        ExplorerRenameResult result =
            _explorerWorkspace.CommitRename(editor.Text);
        if (result.Succeeded)
        {
            ClearExplorerRenameError();
            Announce($"EXPLORER / RENAMED / {result.Name}");
            if (explicitSubmission) FocusExplorerItems();
            return;
        }

        ShowExplorerRenameError(result.Message);
        Announce("EXPLORER / RENAME NEEDS CORRECTION");
        if (!explicitSubmission) return;
        string? renameId = Workspace.RenameItemId;
        QueueExplorerFocus(() =>
        {
            if (Workspace.RenameItemId == renameId && editor.IsLoaded && editor.IsVisible && IsAncestorOf(editor))
            {
                _ = editor.Focus();
                editor.SelectAll();
            }
        });
    }

    private void CancelExplorerRename()
    {
        if (!RequestInteraction()) return;
        if (!_explorerWorkspace.CancelRename())
        {
            return;
        }

        ClearExplorerRenameError();
        Announce("EXPLORER / RENAME CANCELLED");
        FocusExplorerItems();
    }

    private void ShowExplorerPreview(ExplorerPreviewRequest request)
    {
        ExplorerPreviewNameText.Text = request.DisplayName;
        ExplorerPreviewNameText.ToolTip = request.DisplayName;
        AutomationProperties.SetName(
            ExplorerPreviewNameText,
            $"Preview item: {request.DisplayName}");
        ExplorerPreviewTypeText.Text =
            $"{request.TypeLabel} / SESSION OBJECT";
        ExplorerPreviewKindText.Text = request.PreviewKind
            .Replace('-', ' ')
            .ToUpperInvariant();
        ExplorerItemList.IsEnabled = false;
        ExplorerPreviewPanel.Visibility = Visibility.Visible;
        QueueExplorerFocus(() => _ = ExplorerPreviewCloseButton.Focus());
    }

    private void ClearExplorerPreview()
    {
        CancelPendingFocus();
        ExplorerPreviewPanel.Visibility = Visibility.Collapsed;
        ExplorerItemList.IsEnabled = true;
        ExplorerPreviewNameText.Text = string.Empty;
        ExplorerPreviewNameText.ToolTip = null;
        ExplorerPreviewTypeText.Text = string.Empty;
        ExplorerPreviewKindText.Text = string.Empty;
    }

    private void ExplorerPreviewCloseButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        ClearExplorerPreview();
        Announce("EXPLORER / PREVIEW CLOSED");
        FocusExplorerItems();
    }

    private void ExplorerItemList_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_refreshingExplorerPresentation || !RequestInteraction())
        {
            return;
        }

        ClearExplorerRenameError();
        ClearExplorerPreview();
        if (ExplorerItemList.SelectedItem is ExplorerWorkspaceItem item)
        {
            _ = _explorerWorkspace.SelectItem(item.Id);
            Announce($"EXPLORER / SELECTED / {item.Name}");
        }
        else
        {
            _explorerWorkspace.ClearSelection();
            Announce("EXPLORER / SELECTION CLEARED");
        }
    }

    private void ExplorerItemList_OnMouseDoubleClick(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left ||
            FindParent<ListBoxItem>(
                eventArgs.OriginalSource as DependencyObject) is not
                    ListBoxItem container ||
            container.DataContext is not ExplorerWorkspaceItem item)
        {
            return;
        }

        ApplyExplorerOpenResult(_explorerWorkspace.OpenItem(item.Id));
        eventArgs.Handled = true;
    }

    private void ExplorerRenameEditor_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs eventArgs)
    {
        if (!_refreshingExplorerPresentation && sender is TextBox editor)
        {
            CommitExplorerRename(editor, explicitSubmission: false);
        }
    }

    private void ExplorerWindow_OnPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        Key key = eventArgs.Key == Key.System
            ? eventArgs.SystemKey
            : eventArgs.Key;
        if (ExplorerPreviewPanel.Visibility == Visibility.Visible &&
            key == Key.Escape)
        {
            ClearExplorerPreview();
            Announce("EXPLORER / PREVIEW CLOSED");
            FocusExplorerItems();
            eventArgs.Handled = true;
            return;
        }

        if (_explorerWorkspace.RenameItemId is not null &&
            Keyboard.FocusedElement is TextBox editor)
        {
            if (!IsAncestorOf(editor))
            {
                return;
            }

            if (key == Key.Enter)
            {
                CommitExplorerRename(editor);
                eventArgs.Handled = true;
            }
            else if (key == Key.Escape)
            {
                CancelExplorerRename();
                eventArgs.Handled = true;
            }

            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            if (key == Key.Left)
            {
                NavigateExplorerHistory(_explorerWorkspace.TryGoBack);
                eventArgs.Handled = true;
            }
            else if (key == Key.Right)
            {
                NavigateExplorerHistory(_explorerWorkspace.TryGoForward);
                eventArgs.Handled = true;
            }
            else if (key == Key.Up)
            {
                NavigateExplorerHistory(_explorerWorkspace.TryGoUp);
                eventArgs.Handled = true;
            }

            return;
        }

        if (key == Key.N &&
            modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CreateExplorerFolder();
            eventArgs.Handled = true;
            return;
        }

        if (!ExplorerItemList.IsKeyboardFocusWithin)
        {
            return;
        }

        if (key == Key.Enter)
        {
            ApplyExplorerOpenResult(_explorerWorkspace.OpenSelected());
            eventArgs.Handled = true;
        }
        else if (key == Key.F2)
        {
            BeginExplorerRename();
            eventArgs.Handled = true;
        }
        else if (key == Key.Back)
        {
            NavigateExplorerHistory(_explorerWorkspace.TryGoUp);
            eventArgs.Handled = true;
        }
    }

}
