using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Jarvis.Win10.NeuralVoidPreview;

internal static class PartitionScenarios
{
    internal static IReadOnlyList<OwnedWindowLayoutScenario> Run()
    {
        List<OwnedWindowLayoutScenario> scenarios = [];
        Add(scenarios, "partition-rail-four-quadrants-prepares-real-deficit", RealDeficit);
        Add(scenarios, "partition-all-sixteen-real-frames-zero-gap-and-suppressed-surplus", AllTemplates);
        Add(scenarios, "partition-locks-title-and-legacy-mutation-seams", LockedTitles);
        Add(scenarios, "partition-explicit-freeform-command-restores-floating-suppressed-identities", ExitFreeform);
        Add(scenarios, "partition-late-factory-failure-and-batch-admission-noops", TransactionalPreparation);
        Add(scenarios, "partition-taskbar-and-new-swap-without-floating-or-id-reuse", MembershipSwaps);
        Add(scenarios, "partition-hide-refills-exact-pane-excluding-target-and-failure-is-noop", LifecycleRefill);
        Add(scenarios, "partition-divider-range-peer-and-real-focused-arrow-resize", AccessibleResize);
        Add(scenarios, "partition-zoom-exact-resized-composition-and-bounded-template-cache", ZoomAndRatios);
        Add(scenarios, "partition-routed-drag-owns-real-capture-absolute-anchor-and-clamps", CapturedDrag);
        Add(scenarios, "partition-density-crossing-retains-valid-uncommitted-rename-draft", () => DensityRename(false));
        Add(scenarios, "partition-escape-rollback-consumed-before-content-and-stale-gesture-noop", GestureEscape);
        Add(scenarios, "partition-host-deactivation-finishes-capture-before-stale-input", HostDeactivation);
        Add(scenarios, "partition-simultaneous-320x188-real-controls-and-viewports", MinimumUsability);
        Add(scenarios, "partition-all24-connected-divider-hit-spans-and-randomized-model-cuts", TopologyAndRandomized);
        Add(scenarios, "partition-disable-minimize-unload-mode-membership-stale-peer-teardown", TeardownMatrix);
        Add(scenarios, "partition-density-invalid-error-caret-height-preview-and-newer-focus", DensityGuards);

        Add(scenarios, "partition-single-pane-zoom-and-restore-never-exits-mode", SinglePaneZoom);
        Add(scenarios, "partition-rejected-resize-preserves-current-capture-and-full-state", RejectedResize);
        Add(scenarios, "partition-manual-new-preparation-failure-is-honest-and-does-not-consume-id", FailedNew);
        Add(scenarios, "partition-detached-refill-and-late-rail-faults-are-complete-noops", PreparationFaults);
        Add(scenarios, "partition-live-range-notifications-cover-peer-arrow-pointer-and-escape", RangeNotifications);
        Add(scenarios, "freeform-command-restores-floating-maximized-bounds-without-entering-partition", FloatingMaxExit);
        Add(scenarios, "partition-geometry-ratio-admission-rejections-preserve-cache-and-real-frames", NegativeGeometry);
        Add(scenarios, "partition-density-selected-scrolled-rename-and-owned-preview-retention", DensitySelectionPreview);
        Add(scenarios, "partition-rail-validates-projected-geometry-before-real-allocation", ProjectionBeforeFactory);
        Add(scenarios, "partition-composed-density-roundtrip-retains-last-rename-under-newer-search", ComposedDensityRenameSearch);
        foreach (bool last in new[] { false, true })
            foreach (bool invalid in new[] { false, true })
                foreach (string focus in new[] { "owner", "search", "foreign" })
                    Add(scenarios, $"partition-density-matrix-{(last ? "last-scrolled" : "first")}-{(invalid ? "invalid" : "valid")}-{focus}",
                        () => DensityRenameRoundtrip(invalid, last, focus, 2, isolate: true));
        Add(scenarios, "partition-density-direction-backward-first-valid-owner", DensityBackwardSelection);
        foreach (bool backward in new[] { false, true })
            foreach (bool last in new[] { false, true })
                foreach (bool invalid in new[] { false, true })
                    foreach (string focus in new[] { "owner", "search", "foreign" })
                        Add(scenarios, $"partition-density-moving-anchor-{(backward ? "backward" : "forward")}-{(last ? "last-scrolled" : "first")}-{(invalid ? "invalid" : "valid")}-{focus}",
                            () => DensityMovingAnchor(backward, last, invalid, focus));
        return scenarios;
    }

    private static bool DensityBackwardSelection() => WithShell(shell =>
    {
        Require(shell.TaskbarSearchButton.Focus(), "direction setup Search focus failed");
        shell.PreviewLayoutForTest(LayoutPreset.FourQuadrants);
        shell.RaiseRailKeyForTest(Key.Enter); Pump();
        PartitionDivider divider = Dividers(shell).Cast<PartitionDivider>().Single(d => d.Key.Axis == PartitionAxis.Vertical);
        void DragTo(double x)
        {
            PartitionDividerSpan span = Coordinator(shell).Partition!.Dividers.Single(s => s.Key == divider.Key);
            double y = span.Start + (span.End - span.Start) * 0.37;
            BeginDrag(divider, new Point(span.Position, y));
            try { DragMove(divider, new Point(x, y)); shell.UpdateLayout(); Pump(); }
            finally
            {
                SetPointer(divider, new Point(x, y), MouseButtonState.Released);
                divider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.MouseUpEvent });
                SetPointer(divider, null, null);
            }
            Require(Mouse.Captured is null, "direction drag leaked capture");
        }
        DragTo(1048);
        ExplorerSurface owner = shell.GetExplorerSurfaceForTest("explorer");
        ExplorerWorkspaceItem item = owner.Workspace.Items.First(i => i.Kind == ExplorerWorkspaceItemKind.File);
        owner.ExplorerItemList.SelectedItem = owner.ExplorerItemList.Items.OfType<ExplorerWorkspaceItem>().Single(i => i.Id == item.Id);
        Require(owner.ExplorerItemList.Focus(), "direction list focus failed");
        ExplorerWorkspaceScenarios.RaiseKey(owner.ExplorerItemList, Key.F2); Pump();
        TextBox Editor() => ExplorerWorkspaceScenarios.Descendants(owner).OfType<TextBox>().Single(e => e.IsVisible);
        TextBox editor = Editor();
        editor.Text = "UNCOMMITTED_VALID.DOCX";
        editor.Select(7, 0);
        for (int i = 0; i < 3; i++) EditingCommands.SelectLeftByCharacter.Execute(null, editor);
        Require(editor.SelectionStart == 4 && editor.SelectionLength == 3, "backward fixture range invalid");
        DragTo(848);
        TextBox current = Editor();
        Require(current.Text == "UNCOMMITTED_VALID.DOCX" && current.SelectionStart == 4 && current.SelectionLength == 3 &&
            owner.Workspace.RenameItemId == item.Id && owner.Workspace.Items.Single(i => i.Id == item.Id).Name == item.Name &&
            ReferenceEquals(Keyboard.FocusedElement, current), "direction migration lost draft/range/owner or committed blur");
        // Public CaretIndex is the normalized start in both directions. This
        // ordinary WPF Shift+Left command must extend at the moving end, not
        // shrink from an incorrectly normalized forward caret at offset 7.
        EditingCommands.SelectLeftByCharacter.Execute(null, current);
        Require(current.SelectionStart == 3 && current.SelectionLength == 4,
            $"backward moving-end intent lost: subsequent SelectLeft start={current.SelectionStart}, length={current.SelectionLength}; expected 3,4");
        return true;
    });

    private static bool DensityMovingAnchor(bool backward, bool last, bool invalid, string destination) => WithShell(shell =>
    {
        Require(shell.TaskbarSearchButton.Focus(), "moving-anchor setup Search focus failed");
        shell.PreviewLayoutForTest(LayoutPreset.FourQuadrants);
        shell.RaiseRailKeyForTest(Key.Enter); Pump();
        ExplorerSurface owner = shell.GetExplorerSurfaceForTest("explorer");
        ExplorerSurface other = shell.GetExplorerSurfaceForTest("explorer-2");
        ExplorerSurface preview = shell.GetExplorerSurfaceForTest("explorer-3");
        PartitionDivider vertical = Dividers(shell).Cast<PartitionDivider>().Single(d => d.Key.Axis == PartitionAxis.Vertical);
        PartitionDivider horizontal = Dividers(shell).Cast<PartitionDivider>().Single(d => d.Key.Axis == PartitionAxis.Horizontal);
        void Drag(PartitionDivider divider, double[] positions, Action? verify = null)
        {
            PartitionDividerSpan span = Coordinator(shell).Partition!.Dividers.Single(s => s.Key == divider.Key);
            Point At(double value) => divider.Key.Axis == PartitionAxis.Vertical
                ? new Point(value, span.Start + (span.End - span.Start) * 0.37)
                : new Point(span.Start + (span.End - span.Start) * 0.37, value);
            BeginDrag(divider, At(span.Position));
            try
            {
                foreach (double position in positions)
                {
                    ScrollViewer scroll = ExplorerWorkspaceScenarios.Descendants(owner.ExplorerItemList).OfType<ScrollViewer>().First();
                    double offset = scroll.VerticalOffset;
                    bool templateUnchanged = owner.IsCompactWidth;
                    DragMove(divider, At(position)); shell.UpdateLayout(); Pump();
                    if (templateUnchanged == owner.IsCompactWidth)
                        Require(scroll.VerticalOffset == Math.Min(offset, scroll.ScrollableHeight),
                            "moving-anchor changed logical scroll offset beyond WPF viewport clamping");
                    Require(ReferenceEquals(Mouse.Captured, divider) && divider.HasGesture && Dividers(shell).Contains(divider),
                        "moving-anchor lost stable divider/capture");
                    verify?.Invoke();
                }
            }
            finally
            {
                SetPointer(divider, At(positions[^1]), MouseButtonState.Released);
                divider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.MouseUpEvent });
                SetPointer(divider, null, null);
            }
            Require(Mouse.Captured is null, "moving-anchor capture leaked");
            verify?.Invoke();
        }
        Drag(vertical, [1048]); Drag(horizontal, [474]);
        ExplorerWorkspaceItem Target(ExplorerSurface surface) => last
            ? surface.Workspace.Items.Last(i => i.Kind == ExplorerWorkspaceItemKind.File)
            : surface.Workspace.Items.First(i => i.Kind == ExplorerWorkspaceItemKind.File);
        TextBox Editor(ExplorerSurface surface) => ExplorerWorkspaceScenarios.Descendants(surface).OfType<TextBox>().Single(e => e.IsVisible);
        void Range(TextBox editor, int start, int length, bool reverse)
        {
            editor.Select(reverse ? start + length : start, 0);
            for (int i = 0; i < length; i++)
                (reverse ? EditingCommands.SelectLeftByCharacter : EditingCommands.SelectRightByCharacter).Execute(null, editor);
            Require(editor.SelectionStart == start && editor.SelectionLength == length, "moving-anchor setup range invalid");
        }
        void MovingEnd(TextBox editor, int start, int length, bool reverse)
        {
            IInputElement? focus = Keyboard.FocusedElement;
            Require(EditingCommands.SelectLeftByCharacter.CanExecute(null, editor), "moving-anchor Shift navigation unavailable");
            EditingCommands.SelectLeftByCharacter.Execute(null, editor);
            Require(editor.SelectionStart == (reverse ? start - 1 : start) && editor.SelectionLength == (reverse ? length + 1 : length - 1),
                $"{(reverse ? "backward" : "forward")} anchor/moving intent lost: SelectLeft start={editor.SelectionStart}, length={editor.SelectionLength}");
            EditingCommands.SelectRightByCharacter.Execute(null, editor);
            Require(editor.SelectionStart == start && editor.SelectionLength == length && ReferenceEquals(Keyboard.FocusedElement, focus),
                "moving-anchor probe did not restore range or stole focus");
        }
        ExplorerWorkspaceItem previewItem = Target(preview);
        preview.ExplorerItemList.SelectedItem = preview.ExplorerItemList.Items.OfType<ExplorerWorkspaceItem>().Single(i => i.Id == previewItem.Id);
        preview.ApplyExplorerOpenResult(preview.Workspace.OpenSelected()); Pump();
        string previewName = preview.ExplorerPreviewNameText.Text;
        Require(preview.ExplorerPreviewPanel.IsVisible && !preview.ExplorerItemList.IsEnabled, "moving-anchor owned preview fixture absent");
        ExplorerWorkspaceItem otherItem = Target(other);
        other.ExplorerItemList.SelectedItem = other.ExplorerItemList.Items.OfType<ExplorerWorkspaceItem>().Single(i => i.Id == otherItem.Id);
        Require(other.ExplorerItemList.Focus(), "moving-anchor foreign list focus failed");
        ExplorerWorkspaceScenarios.RaiseKey(other.ExplorerItemList, Key.F2); Pump();
        TextBox otherEditor = Editor(other); otherEditor.Text = "OTHER?.VIS";
        ExplorerWorkspaceScenarios.RaiseKey(otherEditor, Key.Enter); Pump();
        Range(otherEditor, 2, 3, !backward);
        string otherError = other.ExplorerRenameErrorText.Text;
        ExplorerWorkspaceItem item = Target(owner);
        owner.ExplorerItemList.SelectedItem = owner.ExplorerItemList.Items.OfType<ExplorerWorkspaceItem>().Single(i => i.Id == item.Id);
        owner.ExplorerItemList.ScrollIntoView(owner.ExplorerItemList.SelectedItem);
        Require(owner.ExplorerItemList.Focus(), "moving-anchor owner list focus failed");
        ExplorerWorkspaceScenarios.RaiseKey(owner.ExplorerItemList, Key.F2); Pump();
        TextBox editor = Editor(owner);
        Require(System.Windows.Data.BindingOperations.IsDataBound(editor, TextBox.TextProperty), "initial rename Name binding absent");
        string draft = invalid || destination != "owner" ? "BAD?.DOCX" : "UNCOMMITTED_VALID.DOCX";
        editor.SelectAll();
        _ = TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, editor, draft));
        Require(editor.Text == draft && System.Windows.Data.BindingOperations.IsDataBound(editor, TextBox.TextProperty),
            "public routed text-composition fixture did not keep the actual OneWay binding");
        if (draft == "BAD?.DOCX") { ExplorerWorkspaceScenarios.RaiseKey(editor, Key.Enter); Pump(); }
        Range(editor, 4, 3, backward);
        string error = owner.ExplorerRenameErrorText.Text;
        Require(!invalid || !string.IsNullOrEmpty(error), "moving-anchor invalid validation missing");
        Require(otherItem.Id == item.Id && !string.IsNullOrEmpty(otherError), "same-seeded-ID/error fixture missing");
        ExplorerSurface[] surfaces = shell.ExplorerSessionIdsForTest.Select(shell.GetExplorerSurfaceForTest).ToArray();
        ExplorerWorkspace[] workspaces = surfaces.Select(s => s.Workspace).ToArray();
        string[] contents = workspaces.Select(w => System.Text.Json.JsonSerializer.Serialize(w.CaptureSnapshot())).ToArray();
        FrameworkElement[] Frames() => shell.ExplorerSessionIdsForTest.Select(id => OwnedWindowLayoutScenarios.ExplorerFrame(shell, id))
            .Cast<FrameworkElement>().Append(shell.TaskManagerWindow).ToArray();
        FrameworkElement[] frames = Frames();
        OwnedWindowLayoutSnapshot state = Coordinator(shell).CaptureSnapshot();
        IInputElement? newer = null;
        void Retained()
        {
            TextBox current = Editor(owner), foreignEditor = Editor(other);
            Require(ReferenceEquals(current, editor) && current.Text == draft && current.SelectionStart == 4 && current.SelectionLength == 3 &&
                owner.Workspace.SelectedItemId == item.Id && owner.Workspace.RenameItemId == item.Id &&
                owner.Workspace.Items.Single(i => i.Id == item.Id).Name == item.Name && owner.ExplorerRenameErrorText.Text == error,
                "moving-anchor density replaced editor/committed blur/lost draft/selection/error");
            MovingEnd(current, 4, 3, backward);
            Require(newer is null ? ReferenceEquals(Keyboard.FocusedElement, current) : ReferenceEquals(Keyboard.FocusedElement, newer),
                "moving-anchor density lost owner or stole newer Search/foreign focus");
            Require(owner.IsAncestorOf(current) && !other.IsAncestorOf(current) && current.DataContext is ExplorerWorkspaceItem target && target.Id == item.Id &&
                ReferenceEquals(current.BorderBrush, owner.AccentBrush) && ReferenceEquals(current.CaretBrush, owner.AccentBrush) &&
                AutomationProperties.GetName(current) == "Rename item" && current.MaxLength == 255 && current.ActualWidth >= 44 && current.ActualHeight == 30,
                "retained editor lost new-row ownership/style/accent/automation/usability");
            Require(ReferenceEquals(foreignEditor, otherEditor) && !ReferenceEquals(current, foreignEditor) && other.IsAncestorOf(foreignEditor) &&
                !owner.IsAncestorOf(foreignEditor) && foreignEditor.DataContext is ExplorerWorkspaceItem foreignItem && foreignItem.Id == item.Id &&
                foreignEditor.Text == "OTHER?.VIS" && foreignEditor.SelectionStart == 2 && foreignEditor.SelectionLength == 3 &&
                other.Workspace.RenameItemId == item.Id && other.ExplorerRenameErrorText.Text == otherError,
                "moving-anchor leaked editor/draft/range/error across same-seeded-ID physical sessions");
            MovingEnd(foreignEditor, 2, 3, !backward);
            Require(preview.ExplorerPreviewPanel.IsVisible && !preview.ExplorerItemList.IsEnabled && preview.ExplorerPreviewNameText.Text == previewName &&
                preview.ExplorerPreviewCloseButton.ActualWidth >= 28 && preview.ExplorerPreviewCloseButton.ActualHeight >= 28,
                "moving-anchor density lost owned preview/name/readiness/close usability");
            ListBoxItem? row = owner.ExplorerItemList.ItemContainerGenerator.ContainerFromItem(owner.ExplorerItemList.SelectedItem) as ListBoxItem;
            ScrollViewer scroll = ExplorerWorkspaceScenarios.Descendants(owner.ExplorerItemList).OfType<ScrollViewer>().First();
            // Shrinking height keeps the logical offset (clamped by WPF), not
            // an auto-scroll policy. A retained focused last row can lie beyond
            // the smaller viewport; its realized editor/selection still survive.
            Require(row is { IsVisible: true, IsLoaded: true } && scroll.VerticalOffset >= 0 && scroll.VerticalOffset <= scroll.ScrollableHeight,
                "moving-anchor lost realized selected/scrolled row or logical scroll context");
            Require(surfaces.SequenceEqual(shell.ExplorerSessionIdsForTest.Select(shell.GetExplorerSurfaceForTest)) && frames.SequenceEqual(Frames()) &&
                workspaces.SequenceEqual(surfaces.Select(s => s.Workspace)) &&
                contents.SequenceEqual(workspaces.Select(w => System.Text.Json.JsonSerializer.Serialize(w.CaptureSnapshot()))) &&
                state.ActiveWindowId == Coordinator(shell).CaptureSnapshot().ActiveWindowId &&
                state.Windows.Select(w => (w.Id, w.AssignedPaneIndex, w.ActivationOrdinal, w.RegistrationOrder, w.Lifecycle, w.FloatingBounds))
                    .SequenceEqual(Coordinator(shell).CaptureSnapshot().Windows.Select(w => (w.Id, w.AssignedPaneIndex, w.ActivationOrdinal, w.RegistrationOrder, w.Lifecycle, w.FloatingBounds))),
                "moving-anchor density changed physical session/workspace/identity/history/content/floating state");
            ExactCover(Coordinator(shell).Partition!.Panes, DesktopShellSurface.OwnedWorkArea);
            ExactCover(frames.Where(f => f.IsVisible).Select(f => f.TransformToAncestor(shell).TransformBounds(new Rect(f.RenderSize))).ToArray(),
                DesktopShellSurface.OwnedWorkArea);
        }
        Retained();
        for (int round = 0; round < 2; round++)
        {
            Drag(vertical, [848, 468, 1048], Retained);
            Drag(horizontal, [374, 212, 474], Retained);
        }
        if (destination != "owner")
        {
            UIElement focusTarget = destination == "search" ? shell.TaskbarSearchButton : other.ExplorerItemList;
            Require(focusTarget.Focus(), "moving-anchor newer focus failed"); Pump();
            newer = Keyboard.FocusedElement;
            Require(ReferenceEquals(newer, focusTarget), "moving-anchor actual newer focus missing");
            if (!invalid) { draft = "UNCOMMITTED_VALID.DOCX"; editor.Text = draft; Range(editor, 4, 3, backward); }
            state = Coordinator(shell).CaptureSnapshot();
        }
        Retained();
        for (int round = 0; round < 2; round++)
        {
            Drag(vertical, [848, 468, 1048], Retained);
            Drag(horizontal, [374, 212, 474], Retained);
        }
        Require(editor.Focus(), "retained editor completion focus failed");
        if (invalid)
        {
            ExplorerWorkspaceScenarios.RaiseKey(editor, Key.Escape); Pump();
            Require(owner.Workspace.RenameItemId is null && owner.Workspace.Items.Single(i => i.Id == item.Id).Name == item.Name &&
                string.IsNullOrEmpty(owner.ExplorerRenameErrorText.Text), "retained editor Escape did not cancel exactly its owner");
        }
        else
        {
            Require(shell.TaskbarSearchButton.Focus(), "ordinary post-density blur setup failed"); Pump();
            Require(owner.Workspace.RenameItemId is null && owner.Workspace.Items.Single(i => i.Id == item.Id).Name == draft &&
                ReferenceEquals(Keyboard.FocusedElement, shell.TaskbarSearchButton), "retained editor broke ordinary valid blur commit or stole newer Search");
        }
        Require(!editor.IsVisible && other.Workspace.RenameItemId == item.Id && otherEditor.Text == "OTHER?.VIS" &&
            other.ExplorerRenameErrorText.Text == otherError, "completed rename left stale retained editor or touched foreign session");
        Require(owner.ExplorerItemList.Focus(), "new rename list focus failed");
        ExplorerWorkspaceScenarios.RaiseKey(owner.ExplorerItemList, Key.F2); Pump();
        TextBox fresh = Editor(owner);
        Require(fresh.Text == (invalid ? item.Name : draft) && System.Windows.Data.BindingOperations.IsDataBound(fresh, TextBox.TextProperty),
            "subsequent rename did not regain current Name binding");
        ExplorerWorkspaceScenarios.RaiseKey(fresh, Key.Escape); Pump();
        return true;
    });

    private static bool ComposedDensityRenameSearch() => DensityRenameRoundtrip(true, true, "search", 1, isolate: false);

    private static bool DensityRenameRoundtrip(bool invalid, bool last, string destination, int repetitions, bool isolate) => WithShell(shell =>
    {
        Require(shell.TaskbarSearchButton.Focus(), "composed density Search setup focus failed");
        shell.PreviewLayoutForTest(LayoutPreset.FourQuadrants);
        shell.RaiseRailKeyForTest(Key.Enter); Pump();
        ExplorerSurface owner = shell.GetExplorerSurfaceForTest("explorer");
        PartitionDivider vertical = Dividers(shell).Cast<PartitionDivider>().Single(d => d.Key.Axis == PartitionAxis.Vertical);
        PartitionDivider horizontal = Dividers(shell).Cast<PartitionDivider>().Single(d => d.Key.Axis == PartitionAxis.Horizontal);
        void Drag(PartitionDivider divider, double[] positions, Action? verify = null)
        {
            PartitionDividerSpan span = Coordinator(shell).Partition!.Dividers.Single(s => s.Key == divider.Key);
            Point At(double position) => divider.Key.Axis == PartitionAxis.Vertical
                ? new Point(position, span.Start + (span.End - span.Start) * 0.37)
                : new Point(span.Start + (span.End - span.Start) * 0.37, position);
            BeginDrag(divider, At(span.Position));
            try
            {
                foreach (double position in positions)
                {
                    DragMove(divider, At(position)); shell.UpdateLayout(); Pump();
                    Require(ReferenceEquals(Mouse.Captured, divider) && divider.HasGesture &&
                        Dividers(shell).Contains(divider), "composed density lost stable divider/capture");
                    verify?.Invoke();
                }
            }
            finally
            {
                SetPointer(divider, At(positions[^1]), MouseButtonState.Released);
                divider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.MouseUpEvent });
                SetPointer(divider, null, null);
            }
            Require(Mouse.Captured is null, "composed density leaked capture");
            verify?.Invoke();
        }
        Drag(vertical, [1048]); Drag(horizontal, [474]);
        ExplorerWorkspaceItem Target(ExplorerSurface surface) => last
            ? surface.Workspace.Items.Last(i => i.Kind == ExplorerWorkspaceItemKind.File)
            : surface.Workspace.Items.First(i => i.Kind == ExplorerWorkspaceItemKind.File);
        TextBox Editor(ExplorerSurface surface) => ExplorerWorkspaceScenarios.Descendants(surface).OfType<TextBox>().Single(e => e.IsVisible);
        ExplorerSurface other = shell.GetExplorerSurfaceForTest("explorer-2");
        string otherError = string.Empty;
        if (isolate)
        {
            other.ExplorerItemList.SelectedItem = other.ExplorerItemList.Items.OfType<ExplorerWorkspaceItem>().Single(i => i.Id == Target(other).Id);
            Require(other.ExplorerItemList.Focus(), "density matrix foreign rename list focus failed");
            ExplorerWorkspaceScenarios.RaiseKey(other.ExplorerItemList, Key.F2); Pump();
            TextBox otherEditor = Editor(other); otherEditor.Text = "OTHER?.VIS";
            ExplorerWorkspaceScenarios.RaiseKey(otherEditor, Key.Enter); Pump();
            otherEditor.Select(2, 3); otherError = other.ExplorerRenameErrorText.Text;
            Require(!string.IsNullOrEmpty(otherError), "density matrix foreign validation fixture missing");
        }
        ExplorerWorkspaceItem item = Target(owner);
        owner.ExplorerItemList.SelectedItem = owner.ExplorerItemList.Items.OfType<ExplorerWorkspaceItem>().Single(i => i.Id == item.Id);
        owner.ExplorerItemList.ScrollIntoView(owner.ExplorerItemList.SelectedItem);
        Require(owner.ExplorerItemList.Focus(), "composed density list focus failed");
        ExplorerWorkspaceScenarios.RaiseKey(owner.ExplorerItemList, Key.F2); Pump();
        TextBox editor = Editor(owner);
        Require(ReferenceEquals(Keyboard.FocusedElement, editor), "composed density F2 did not focus owning editor");
        // Existing valid blur commits intentionally. For the valid/unfocused
        // fixture, leave an invalid rename before giving newer focus, then edit
        // its real unfocused TextBox to a valid unsubmitted draft (no blur bypass).
        string draft = invalid || destination != "owner" ? "BAD?.DOCX" : "UNCOMMITTED_VALID.DOCX";
        editor.Text = draft;
        if (draft == "BAD?.DOCX") { ExplorerWorkspaceScenarios.RaiseKey(editor, Key.Enter); Pump(); }
        editor.Select(4, 3);
        int caret = editor.CaretIndex;
        string error = owner.ExplorerRenameErrorText.Text;
        Require(!last || item.Id == "file:site-map", "composed density selected target differs from native RED");
        Require(draft != "BAD?.DOCX" || !string.IsNullOrEmpty(error), "composed density validation fixture missing");
        ExplorerSurface[] surfaces = shell.ExplorerSessionIdsForTest.Select(shell.GetExplorerSurfaceForTest).ToArray();
        FrameworkElement[] Frames() => shell.ExplorerSessionIdsForTest.Select(id => OwnedWindowLayoutScenarios.ExplorerFrame(shell, id))
            .Cast<FrameworkElement>().Append(shell.TaskManagerWindow).ToArray();
        FrameworkElement[] frames = Frames();
        ExplorerWorkspace[] workspaces = surfaces.Select(s => s.Workspace).ToArray();
        string[] contents = workspaces.Select(w => System.Text.Json.JsonSerializer.Serialize(w.CaptureSnapshot())).ToArray();
        OwnedWindowLayoutSnapshot state = Coordinator(shell).CaptureSnapshot();
        IInputElement? newer = null;
        void Retained()
        {
            TextBox current = Editor(owner);
            Require(current.Text == draft && current.SelectionStart == 4 && current.SelectionLength == 3 && current.CaretIndex == caret,
                $"composed density lost draft/caret/selection: draft={current.Text}, start={current.SelectionStart}, length={current.SelectionLength}, caret={current.CaretIndex}; focus={(Keyboard.FocusedElement as FrameworkElement)?.Name}");
            Require(owner.IsAncestorOf(current) && current.DataContext is ExplorerWorkspaceItem target && target.Id == item.Id &&
                owner.Workspace.RenameItemId == item.Id && owner.Workspace.SelectedItemId == item.Id &&
                owner.ExplorerRenameErrorText.Text == error && owner.Workspace.Items.Single(i => i.Id == item.Id).Name == item.Name,
                "composed density committed blur/lost target/error");
            Require(newer is null ? ReferenceEquals(Keyboard.FocusedElement, current) : ReferenceEquals(Keyboard.FocusedElement, newer),
                "composed density lost owning focus or stole newer Search/foreign focus");
            if (isolate)
            {
                TextBox otherEditor = Editor(other);
                Require(!ReferenceEquals(current, otherEditor) && other.IsAncestorOf(otherEditor) && !owner.IsAncestorOf(otherEditor) &&
                    otherEditor.DataContext is ExplorerWorkspaceItem otherItem && otherItem.Id == item.Id &&
                    other.Workspace.RenameItemId == item.Id && otherEditor.Text == "OTHER?.VIS" &&
                    otherEditor.SelectionStart == 2 && otherEditor.SelectionLength == 3 && otherEditor.CaretIndex == 2 &&
                    other.ExplorerRenameErrorText.Text == otherError,
                    "density copied draft/caret/error across physical surfaces sharing the seeded item ID");
            }
            Require(surfaces.SequenceEqual(shell.ExplorerSessionIdsForTest.Select(shell.GetExplorerSurfaceForTest)) && frames.SequenceEqual(Frames()) &&
                workspaces.SequenceEqual(surfaces.Select(s => s.Workspace)) &&
                contents.SequenceEqual(workspaces.Select(w => System.Text.Json.JsonSerializer.Serialize(w.CaptureSnapshot()))) &&
                state.ActiveWindowId == Coordinator(shell).CaptureSnapshot().ActiveWindowId &&
                state.Windows.Select(s => (s.Id, s.AssignedPaneIndex, s.ActivationOrdinal, s.RegistrationOrder, s.Lifecycle, s.FloatingBounds))
                    .SequenceEqual(Coordinator(shell).CaptureSnapshot().Windows.Select(s => (s.Id, s.AssignedPaneIndex, s.ActivationOrdinal, s.RegistrationOrder, s.Lifecycle, s.FloatingBounds))),
                "composed density changed physical session/workspace/identity/history/floating/content");
            ExactCover(Coordinator(shell).Partition!.Panes, DesktopShellSurface.OwnedWorkArea);
            ExactCover(frames.Where(frame => frame.IsVisible).Select(frame => frame.TransformToAncestor(shell)
                .TransformBounds(new Rect(frame.RenderSize))).ToArray(), DesktopShellSurface.OwnedWorkArea);
        }
        Retained();
        for (int round = 0; round < repetitions; round++)
        {
            Drag(vertical, [848, 468, 1048], Retained);
            Drag(horizontal, [374, 212, 474], Retained);
        }
        if (destination != "owner")
        {
            UIElement focusTarget = destination == "search" ? shell.TaskbarSearchButton : other.ExplorerItemList;
            Require(focusTarget.Focus(), $"composed density newer {destination} focus failed"); Pump();
            newer = Keyboard.FocusedElement;
            Require(ReferenceEquals(newer, focusTarget), $"composed density actual {destination} focus missing");
            if (!invalid)
            {
                draft = "UNCOMMITTED_VALID.DOCX";
                Editor(owner).Text = draft; Editor(owner).Select(4, 3);
            }
            // The foreign focus transition intentionally changes activation;
            // every subsequent resize must preserve that newer state exactly.
            state = Coordinator(shell).CaptureSnapshot();
        }
        Retained();
        for (int round = 0; round < repetitions; round++)
        {
            Drag(vertical, isolate ? [848, 468, 1048] : [848, 1048], Retained);
            Drag(horizontal, isolate ? [374, 212, 474] : [374, 474], Retained);
        }
        return true;
    });

    private static bool ProjectionBeforeFactory()
    {
        int allocations = 0;
        DesktopShellSurface supplied = new(number => { allocations++; return new ExplorerSurface(); });
        return WithShell(shell =>
        {
            var cache = (Dictionary<LayoutPreset,IReadOnlyDictionary<PartitionCutKey,double>>)typeof(OwnedWindowLayoutCoordinator)
                .GetField("_partitionRatios",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(Coordinator(shell))!;
            cache[LayoutPreset.FourQuadrants] = new Dictionary<PartitionCutKey,double>
            {
                [new(PartitionAxis.Vertical,6)] = 0,
                [new(PartitionAxis.Horizontal,6)] = 0.5,
            };
            string before = Fingerprint(shell);
            shell.PreviewLayoutForTest(LayoutPreset.FourQuadrants); shell.RaiseRailKeyForTest(Key.Enter);
            Require(allocations == 0 && before == Fingerprint(shell) && shell.IsOwnedWindowLayoutFreeform &&
                shell.InteractionStatusForTest.Contains("NO CHANGE",StringComparison.Ordinal),
                $"rail allocated before validating collapsed projection: allocations={allocations}");
            return true;
        },supplied);
    }

    private static bool NegativeGeometry() => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.FourQuadrants);
        string admissionBefore = Fingerprint(shell);
        Require(!Coordinator(shell).TryCommitPartition(LayoutPreset.Maximized,DesktopShellSurface.OwnedWorkArea,
            new[] { new OwnedWindowAdmission(new("explorer-4"),new Rect(512,117,930,667)) }).Succeeded &&
            admissionBefore == Fingerprint(shell),"batch admitted more than the exact real deficit");
        foreach (LayoutDefinition definition in LayoutCatalog.All)
        {
            _ = shell.SelectLayoutForTest(definition.Preset); Pump();
            OwnedWindowLayoutCoordinator model = Coordinator(shell);
            string before = Fingerprint(shell);
            foreach (Rect bad in new[] { new Rect(1e308,24,1e308,752),new Rect(0,24,5e307,752),
                new Rect(148.1,24,1428,752),new Rect(148,24,319,187) })
                Require(!model.TryCommitPartition(definition.Preset,bad,Array.Empty<OwnedWindowAdmission>()).Succeeded &&
                    Fingerprint(shell) == before,"invalid projection changed cache, assignment or WPF frames");
            Require(!model.TryCommitPartition((LayoutPreset)999,DesktopShellSurface.OwnedWorkArea,Array.Empty<OwnedWindowAdmission>()).Succeeded &&
                !model.TryShowPartition(new(" explorer-4 "),new(new(" explorer-4 "),new Rect(512,117,930,667))).Succeeded &&
                before == Fingerprint(shell),"unknown preset or malformed identity normalized/admitted");
            PartitionGeometry geometry = model.Partition!;
            foreach (PartitionCutKey key in geometry.Ratios.Keys)
            {
                foreach (double invalid in new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity,-0.1,1.1,0,1 })
                {
                    Dictionary<PartitionCutKey,double> ratios = geometry.Ratios.ToDictionary(pair => pair.Key,pair => pair.Value);
                    ratios[key] = invalid;
                    Require(!PartitionGeometry.TryCreate(definition.Preset,geometry.WorkArea,ratios,out _) && before == Fingerprint(shell),
                        "invalid/collapsed saved cuts accepted or mutated current state");
                }
                Dictionary<PartitionCutKey,double> missing = geometry.Ratios.ToDictionary(pair => pair.Key,pair => pair.Value); missing.Remove(key);
                Require(!PartitionGeometry.TryCreate(definition.Preset,geometry.WorkArea,missing,out _) &&
                    !model.TryResizePartition(key,double.NaN,model.PartitionGeneration).Succeeded &&
                    !model.TryResizePartition(key,900,model.PartitionGeneration-1).Succeeded && before == Fingerprint(shell),"missing/stale resize changed state");
            }
        }
        Require(!PartitionGeometry.IsExactCover(new[] { new Rect(148,24,800,752),new Rect(862,24,714,752) },DesktopShellSurface.OwnedWorkArea),
            "positive overlap falsely accepted");
        Require(!PartitionGeometry.IsExactCover(new[] { new Rect(148,24,700,752),new Rect(862,24,714,752) },DesktopShellSurface.OwnedWorkArea),
            "hole falsely accepted");
        return true;
    });

    private static bool DensitySelectionPreview() => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.FourQuadrants); Pump();
        PartitionDivider vertical = Dividers(shell).Cast<PartitionDivider>().Single(divider => divider.Key.Axis == PartitionAxis.Vertical);
        PartitionDivider horizontal = Dividers(shell).Cast<PartitionDivider>().Single(divider => divider.Key.Axis == PartitionAxis.Horizontal);
        vertical.SetRatio(0.7); horizontal.SetRatio(horizontal.Limits.Minimum); Pump();
        ExplorerSurface owner = shell.GetExplorerSurfaceForTest("explorer");
        ExplorerWorkspaceItem lastFile = owner.Workspace.Items.Last(item => item.Kind == ExplorerWorkspaceItemKind.File);
        owner.ExplorerItemList.SelectedItem = owner.ExplorerItemList.Items.OfType<ExplorerWorkspaceItem>().Single(item => item.Id == lastFile.Id);
        _ = owner.ExplorerItemList.Focus(); ExplorerWorkspaceScenarios.RaiseKey(owner.ExplorerItemList,Key.F2); Pump();
        TextBox editor = (TextBox)Keyboard.FocusedElement!; editor.Text = "SCROLLED_UNCOMMITTED.DOCX"; editor.Select(3,5);
        foreach (double ratio in new[] { 0.5,0.7,vertical.Limits.Minimum,0.7 })
        {
            vertical.SetRatio(ratio); shell.UpdateLayout(); Pump();
            TextBox retained = (TextBox)Keyboard.FocusedElement!;
            Require(owner.Workspace.SelectedItemId == lastFile.Id && owner.Workspace.RenameItemId == lastFile.Id &&
                retained.Text == "SCROLLED_UNCOMMITTED.DOCX" && retained.SelectionStart == 3 && retained.SelectionLength == 5 &&
                owner.Workspace.Items.Single(item => item.Id == lastFile.Id).Name == lastFile.Name,
                "density lost selected/scrolled rename draft or caret");
            Rect bounds = retained.TransformToAncestor(shell).TransformBounds(new Rect(retained.RenderSize));
            Require(shell.GetOwnedWindowBoundsForTest("explorer").Contains(bounds),"scrolled selected editor fell outside pane");
        }
        ExplorerWorkspaceScenarios.RaiseKey((UIElement)Keyboard.FocusedElement!,Key.Escape); Pump();
        owner.ExplorerOpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        string preview = owner.ExplorerPreviewNameText.Text;
        Require(owner.ExplorerPreviewPanel.IsVisible && preview == lastFile.Name,"owned preview fixture not open");
        _ = shell.TaskbarSearchButton.Focus();
        foreach (double ratio in new[] { 0.5,0.7,vertical.Limits.Minimum })
        {
            vertical.SetRatio(ratio); Pump();
            Require(owner.ExplorerPreviewPanel.IsVisible && owner.ExplorerPreviewNameText.Text == preview &&
                owner.Workspace.SelectedItemId == lastFile.Id && shell.TaskbarSearchButton.IsKeyboardFocused &&
                owner.ExplorerPreviewCloseButton.IsVisible && owner.ExplorerPreviewCloseButton.ActualHeight >= 28,
                "width density lost owned preview or stole newer focus");
        }
        foreach (double ratio in new[] { 0.6,horizontal.Limits.Minimum,0.6 })
        {
            horizontal.SetRatio(ratio); Pump();
            Require(owner.ExplorerPreviewPanel.IsVisible && owner.ExplorerPreviewNameText.Text == preview &&
                shell.TaskbarSearchButton.IsKeyboardFocused,"height density lost preview/focus");
        }
        return true;
    });

    private static bool FloatingMaxExit() => WithShell(shell =>
    {
        Rect floating = shell.GetOwnedWindowBoundsForTest("explorer");
        ExplorerSurface original = shell.GetExplorerSurfaceForTest("explorer");
        shell.ExplorerMaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(!Coordinator(shell).IsPartition && shell.IsExplorerMaximized,"floating maximize fixture wrong");
        _ = shell.TaskbarSearchButton.Focus();
        Exit(shell); Pump();
        Require(shell.IsOwnedWindowLayoutFreeform && shell.CurrentLayoutGlyph.IsFreeform &&
            shell.GetOwnedWindowBoundsForTest("explorer") == floating && shell.TaskbarSearchButton.IsKeyboardFocused &&
            !shell.IsTaskManagerVisibleForTest && shell.ExplorerSessionIdsForTest.Count == 1 &&
            ReferenceEquals(original,shell.GetExplorerSurfaceForTest("explorer")),
            "Freeform command left the floating window maximized or reopened/created another identity");
        return true;
    });

    private static bool RangeNotifications() => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns); Pump();
        PartitionDivider divider = (PartitionDivider)Dividers(shell).Single();
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(divider)!;
        var range = (System.Windows.Automation.Provider.IRangeValueProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.RangeValue)!;
        var observable = System.ComponentModel.DependencyPropertyDescriptor.FromName("Value",typeof(PartitionDivider),typeof(PartitionDivider));
        Require(observable is not null,"live ratio is not observable across arrow/pointer changes");
        List<double> values = [];
        EventHandler changed = (_,_) => values.Add(range.Value);
        observable!.AddValueChanged(divider,changed);
        try
        {
            range.SetValue(0.6);
            _ = divider.Focus(); ExplorerWorkspaceScenarios.RaiseKey(divider,Key.Right);
            _ = shell.TaskbarSearchButton.Focus();
            double position = Coordinator(shell).Partition!.Dividers.Single().Position;
            BeginDrag(divider,new Point(position,300));
            DragMove(divider,new Point(position+17,300));
            DragMove(divider,new Point(position+17,300));
            ExplorerWorkspaceScenarios.RaiseKey(shell.TaskbarSearchButton,Key.Escape);
            Require(values.Count == 4 && values.All(double.IsFinite) && values[1] > values[0] &&
                values[2] > values[1] && values[3] == values[1] && range.Value == values[3],
                "ratio notifications missed a real input route or reported duplicate/stale values");
        }
        finally { observable.RemoveValueChanged(divider,changed); }
        return true;
    });

    private static bool PreparationFaults()
    {
        foreach (bool rail in new[] { false,true })
        {
            int attempts = 0;
            DesktopShellSurface supplied = new(number =>
            {
                attempts++;
                if (!rail || number == 3) throw new ArgumentException("injected later detached factory fault");
                return new ExplorerSurface();
            });
            Require(WithShell(shell =>
            {
                if (!rail) _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
                string before = Fingerprint(shell);
                UIElement[] children = shell.DesktopCanvas.Children.Cast<UIElement>().ToArray();
                try
                {
                    if (rail) { shell.PreviewLayoutForTest(LayoutPreset.FourQuadrants); shell.RaiseRailKeyForTest(Key.Enter); }
                    else shell.ExplorerCloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (ArgumentException) { Require(false,"preparation fault escaped instead of rejecting entire command"); }
                Require(attempts == (rail ? 2 : 1) && before == Fingerprint(shell) &&
                    children.SequenceEqual(shell.DesktopCanvas.Children.Cast<UIElement>()) &&
                    shell.InteractionStatusForTest.Contains("FAILED",StringComparison.Ordinal),"factory fault partially admitted/hidden/counted");
                return true;
            },supplied),"transactional factory fault trace failed");
        }
        return true;
    }

    private static bool FailedNew()
    {
        int attempts = 0;
        DesktopShellSurface supplied = new(number => ++attempts == 1
            ? throw new ArgumentException("injected detached preparation failure") : new ExplorerSurface());
        return WithShell(shell =>
        {
            _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns); Pump();
            string before = Fingerprint(shell);
            UIElement[] children = shell.DesktopCanvas.Children.Cast<UIElement>().ToArray();
            MenuItem create = (MenuItem)shell.TaskbarExplorerButton.ContextMenu.Items[0];
            try { create.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
            catch (ArgumentException) { Require(false,"explicit New leaked factory failure instead of honest no-change"); }
            Require(before == Fingerprint(shell) && children.SequenceEqual(shell.DesktopCanvas.Children.Cast<UIElement>()) &&
                shell.InteractionStatusForTest.Contains("FAILED",StringComparison.Ordinal),
                "failed New consumed identity/slot or partially changed geometry");
            create.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Require(attempts == 2 && shell.ExplorerSessionIdsForTest.SequenceEqual(new[] { "explorer","explorer-2" }) &&
                shell.VisibleOwnedWindowCount == 2,"failed unpublished ID was consumed/recycled incorrectly");
            return true;
        },supplied);
    }

    private static bool RejectedResize() => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns); Pump();
        _ = shell.TaskbarSearchButton.Focus();
        PartitionDivider divider = (PartitionDivider)Dividers(shell).Single();
        BeginDrag(divider,new Point(862,300)); DragMove(divider,new Point(900,300));
        foreach (var request in new[]
        {
            (divider.Key,double.NaN,shell.PartitionEpoch),
            (divider.Key,double.PositiveInfinity,shell.PartitionEpoch),
            (new PartitionCutKey(PartitionAxis.Vertical,7),900d,shell.PartitionEpoch),
            (divider.Key,900d,shell.PartitionEpoch-1),
        })
        {
            string before = Fingerprint(shell);
            Require(!shell.ResizePartition(request.Item1,request.Item2,request.Item3) &&
                divider.HasGesture && ReferenceEquals(Mouse.Captured,divider) && before == Fingerprint(shell),
                "rejected/stale resize canceled live capture or changed state");
        }
        DragMove(divider,new Point(920,300)); shell.UpdateLayout();
        Require(shell.GetOwnedWindowBoundsForTest("explorer").Width == 772 && shell.TaskbarSearchButton.IsKeyboardFocused,
            "rejection lost absolute anchor or content focus");
        divider.ReleaseMouseCapture();
        return true;
    });

    private static bool SinglePaneZoom() => WithShell(shell =>
    {
        shell.PreviewLayoutForTest(LayoutPreset.Maximized);
        shell.RaiseRailKeyForTest(Key.Enter); Pump();
        ExplorerSurface original = shell.GetExplorerSurfaceForTest("explorer");
        string[] ids = shell.ExplorerSessionIdsForTest.ToArray();
        _ = shell.TaskbarSearchButton.Focus();
        for (int toggle=0;toggle<2;toggle++)
        {
            shell.ExplorerMaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(Coordinator(shell).IsPartition && shell.CurrentLayout == LayoutPreset.Maximized &&
                !shell.CurrentLayoutGlyph.IsFreeform && shell.GetOwnedWindowBoundsForTest("explorer") == DesktopShellSurface.OwnedWorkArea &&
                shell.TaskbarSearchButton.IsKeyboardFocused && ids.SequenceEqual(shell.ExplorerSessionIdsForTest) &&
                ReferenceEquals(original,shell.GetExplorerSurfaceForTest("explorer")),
                "single-pane title restore silently left Partition or allocated/lost focus");
        }
        return true;
    });

    private static bool RealDeficit() => WithShell(shell =>
    {
        ExplorerSurface primary = shell.GetExplorerSurfaceForTest("explorer");
        string? active = shell.ActiveOwnedWindowId;
        shell.PreviewLayoutForTest(LayoutPreset.FourQuadrants);
        shell.FocusLayoutRailForTest();
        Pump();
        IInputElement? focus = Keyboard.FocusedElement;
        shell.RaiseRailKeyForTest(Key.Enter);
        shell.UpdateLayout();
        Pump();
        Require(shell.ExplorerSessionIdsForTest.SequenceEqual(new[] { "explorer", "explorer-2", "explorer-3" }) &&
            shell.VisibleOwnedWindowCount == 4,
            $"real rail deficit missing: retainedExplorers={shell.ExplorerSessionIdsForTest.Count}, visible={shell.VisibleOwnedWindowCount}, required=4");
        Require(ReferenceEquals(primary, shell.GetExplorerSurfaceForTest("explorer")) &&
            shell.ActiveOwnedWindowId == active && ReferenceEquals(Keyboard.FocusedElement, focus), "rail changed identity/active/focus");
        ExplorerSurface[] surfaces = shell.ExplorerSessionIdsForTest.Select(shell.GetExplorerSurfaceForTest).ToArray();
        Require(surfaces.Distinct().Count() == 3 && surfaces.Select(s => s.Workspace).Distinct().Count() == 3 &&
            surfaces.All(s => s.IsVisible && s.ExplorerItemList.Items.Count == 6 && s.ExplorerActionPanel.IsVisible) &&
            shell.TaskManagerWindow.IsVisible && shell.IsTaskManagerSamplingForTest, "model IDs are not real populated independent controls");
        return true;
    });

    private static bool AllTemplates() => WithShell(shell =>
    {
        for (int index = 0; index < 3; index++) _ = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        Require(shell.ExplorerSessionIdsForTest.Count == 4, "fixture cap not reached");
        _ = shell.SelectLayoutForTest(LayoutPreset.FourQuadrants);
        shell.UpdateLayout();
        ExactCover(shell.ExplorerSessionIdsForTest.Select(id => shell.GetOwnedWindowBoundsForTest(id)).ToArray(), DesktopShellSurface.OwnedWorkArea);
        _ = shell.TaskbarSearchButton.Focus();
        foreach (LayoutDefinition definition in LayoutCatalog.All)
        {
            shell.PreviewLayoutForTest(definition.Preset);
            shell.RaiseRailKeyForTest(Key.Enter);
            shell.UpdateLayout(); Pump();
            Rect[] frames = shell.ExplorerSessionIdsForTest.Select(id => OwnedWindowLayoutScenarios.ExplorerFrame(shell, id))
                .Cast<FrameworkElement>().Append(shell.TaskManagerWindow).Where(frame => frame.IsVisible)
                .Select(frame => frame.TransformToAncestor(shell).TransformBounds(new Rect(frame.RenderSize))).ToArray();
            Require(frames.Length == definition.PaneCount && shell.VisibleOwnedWindowCount == definition.PaneCount,
                $"{definition.Preset}: surplus visible/partial {frames.Length}/{definition.PaneCount}");
            ExactCover(frames, DesktopShellSurface.OwnedWorkArea);
            Require(shell.TaskbarSearchButton.IsKeyboardFocused && shell.ExplorerSessionIdsForTest.Count == 4,
                "template replacement stole focus or exceeded cap");
        }
        return true;
    });

    private static bool MinimumUsability()
    {
        foreach (string active in new[] { "explorer","task-manager" })
            Require(WithShell(shell =>
            {
                _ = shell.PrepareTaskManagerForSnapshot();
                if (active == "explorer") _ = shell.ExplorerItemList.Focus();
                _ = shell.SelectLayoutForTest(LayoutPreset.FourQuadrants); Pump();
                foreach (PartitionDivider divider in Dividers(shell).Cast<PartitionDivider>()) divider.SetRatio(divider.Limits.Minimum);
                shell.UpdateLayout(); Pump();
                Rect pane = shell.GetOwnedWindowBoundsForTest(active);
                Require(pane.Width == 320 && pane.Height == 188,$"{active}: simultaneous minimum not reached: {pane}");
                FrameworkElement[] controls = active == "explorer" ? new FrameworkElement[]
                {
                    shell.ExplorerMinimizeButton,shell.ExplorerMaximizeButton,shell.ExplorerCloseButton,
                    shell.ExplorerBackButton,shell.ExplorerForwardButton,shell.ExplorerUpButton,
                    shell.ExplorerOpenButton,shell.ExplorerRenameButton,shell.ExplorerNewFolderButton,
                } : new FrameworkElement[] { shell.TaskManagerMinimizeButton,shell.TaskManagerMaximizeButton,shell.TaskManagerCloseButton };
                foreach (FrameworkElement control in controls)
                {
                    Rect bounds = control.TransformToAncestor(shell).TransformBounds(new Rect(control.RenderSize));
                    Require(control.IsVisible && bounds.Width >= 28 && bounds.Height >= 28 && pane.Contains(bounds),$"{active}/{control.Name}: minimum control {bounds} outside {pane}");
                }
                Require(active == "explorer" ? shell.ExplorerItemList.ActualHeight >= 44 && shell.ExplorerItemList.Items.Count == 6
                    : shell.TaskManagerPanel.ProcessViewportHeightForTest >= 43 && shell.TaskManagerPanel.ProcessRows.Count == 8,
                    $"{active}: minimum viewport floor rejected");
                return true;
            }),$"{active}: minimum usability failed");
        return true;
    }

    private static bool TopologyAndRandomized()
    {
        int cuts = 0, deformations = 0;
        Random random = new(41716);
        OwnedWindowLayoutCoordinator coordinator = new();
        _ = coordinator.TryRegister(new("explorer"),OwnedWindowLifecycle.Visible,new Rect(596,63,930,667));
        _ = coordinator.TryRegister(new("task-manager"),OwnedWindowLifecycle.Closed,new Rect(720,104,760,600));
        foreach (int number in new[] { 2,3,4 }) _ = coordinator.TryRegister(new($"explorer-{number}"),OwnedWindowLifecycle.Closed,new Rect(596-28*(number-1),63+18*(number-1),930,667));
        foreach (LayoutDefinition definition in LayoutCatalog.All)
        {
            Require(coordinator.TryCommitPartition(definition.Preset,DesktopShellSurface.OwnedWorkArea,Array.Empty<OwnedWindowAdmission>()).Succeeded,"catalog commit rejected");
            PartitionGeometry geometry = coordinator.Partition!;
            cuts += geometry.Dividers.Count;
            foreach (PartitionCutKey key in geometry.Ratios.Keys)
                for (int sample=0;sample<128;sample++)
                {
                    OwnedWindowState[] before = coordinator.CaptureSnapshot().Windows.ToArray();
                    double position = key.Axis == PartitionAxis.Vertical ? -500+random.NextDouble()*2500 : -500+random.NextDouble()*1800;
                    Require(coordinator.TryResizePartition(key,position,coordinator.PartitionGeneration).Succeeded,"finite legal clamp rejected");
                    geometry = coordinator.Partition!; ExactCover(geometry.Panes,geometry.WorkArea); deformations++;
                    foreach (OwnedWindowState original in before)
                    {
                        OwnedWindowState current = coordinator.CaptureSnapshot().Windows.Single(state => state.Id == original.Id);
                        Require(current.Id == original.Id && current.AssignedPaneIndex == original.AssignedPaneIndex &&
                            current.FloatingBounds == original.FloatingBounds && current.ActivationOrdinal == original.ActivationOrdinal,"random resize changed identity/history");
                        if (original.AssignedPaneIndex is int index)
                        {
                            LayoutPane pane = definition.Panes[index];
                            bool touched = key.Axis == PartitionAxis.Vertical ? pane.X == key.GridCoordinate || pane.Right == key.GridCoordinate
                                : pane.Y == key.GridCoordinate || pane.Bottom == key.GridCoordinate;
                            Require(touched || original.Bounds == current.Bounds,"unrelated pane moved");
                        }
                    }
                }
        }
        Require(cuts == 24 && deformations == 3072,$"cut coverage counts differ: {cuts}/{deformations}");
        return WithShell(shell =>
        {
            foreach (LayoutDefinition definition in LayoutCatalog.All)
            {
                _ = shell.SelectLayoutForTest(definition.Preset); Pump(); shell.UpdateLayout();
                PartitionGeometry geometry = Coordinator(shell).Partition!;
                Require(Dividers(shell).Length == geometry.Dividers.Count,"WPF cut topology differs");
                foreach (PartitionDivider divider in Dividers(shell).Cast<PartitionDivider>())
                {
                    PartitionDividerSpan span = geometry.Dividers.Single(item => item.Key == divider.Key);
                    bool vertical = span.Key.Axis == PartitionAxis.Vertical;
                    foreach (double along in new[] { span.Start+0.5,(span.Start+span.End)/2,span.End-0.5 })
                        foreach (double offset in new[] { -3.5,0,3.5 })
                        {
                            Point point = vertical ? new Point(span.Position+offset,along) : new Point(along,span.Position+offset);
                            Require(shell.DesktopCanvas.InputHitTest(point) is PartitionDivider,$"{definition.Preset}/{span.Key}: seam hit missing at {point}");
                        }
                    string? active = shell.ActiveOwnedWindowId;
                    Require(divider.Focus() && shell.ActiveOwnedWindowId == active,"Tab-reachable divider activates pane");
                }
                foreach (Rect pane in geometry.Panes)
                    Require(shell.DesktopCanvas.InputHitTest(new Point(pane.Left+pane.Width/2,pane.Top+pane.Height/2)) is not PartitionDivider,"overlay steals body");
                foreach (PartitionDivider divider in Dividers(shell).Cast<PartitionDivider>())
                {
                    PartitionGeometry current = Coordinator(shell).Partition!;
                    PartitionDividerSpan span = current.Dividers.Single(item => item.Key == divider.Key);
                    Require(current.TryGetLimits(divider.Key,out _,out double maximum),"key clamp unavailable");
                    string? active = shell.ActiveOwnedWindowId;
                    _ = divider.Focus();
                    ExplorerWorkspaceScenarios.RaiseKey((UIElement)Keyboard.FocusedElement!,divider.Key.Axis == PartitionAxis.Vertical ? Key.Right : Key.Down);
                    Require(Coordinator(shell).Partition!.Dividers.Single(item => item.Key == divider.Key).Position == Math.Min(maximum,span.Position+8) &&
                        shell.ActiveOwnedWindowId == active,"catalog focused arrow axis/step/clamp changed owner");
                }
                Require(shell.DesktopCanvas.InputHitTest(new Point(100,200)) is not PartitionDivider &&
                    shell.DesktopCanvas.InputHitTest(new Point(200,840)) is not PartitionDivider,"overlay steals rail/taskbar");
            }
            return true;
        });
    }

    private static bool TeardownMatrix()
    {
        foreach (string transition in new[] { "disable","minimize-host","unload","freeform","template","zoom","lifecycle","new" })
            Require(WithShell(shell =>
            {
                _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns); Pump();
                PartitionDivider divider = (PartitionDivider)Dividers(shell).Single();
                var range = (System.Windows.Automation.Provider.IRangeValueProvider)System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(divider)!
                    .GetPattern(System.Windows.Automation.Peers.PatternInterface.RangeValue)!;
                BeginDrag(divider,new Point(862,300)); DragMove(divider,new Point(900,300));
                switch (transition)
                {
                    case "disable": divider.IsEnabled = false; break;
                    case "minimize-host": Window.GetWindow(shell)!.WindowState = WindowState.Minimized; Pump(); break;
                    case "unload": Window.GetWindow(shell)!.Content = null; Pump(); break;
                    case "freeform": Exit(shell); break;
                    case "template": _ = shell.SelectLayoutForTest(LayoutPreset.EqualRows); break;
                    case "zoom": shell.ExplorerMaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                    case "lifecycle": shell.ExplorerMinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                    default: _ = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell); break;
                }
                Require(Mouse.Captured is null && !divider.HasGesture,$"{transition}: capture/intent leaked");
                string current = Fingerprint(shell);
                DragMove(divider,new Point(1000,300));
                divider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
                Require(current == Fingerprint(shell),$"{transition}: stale pointer changed newer state");
                if (!divider.IsCurrent)
                {
                    try { range.SetValue(0.5); throw new InvalidOperationException("stale automation accepted"); }
                    catch (System.Windows.Automation.ElementNotEnabledException) { }
                    Require(current == Fingerprint(shell),"stale peer changed state");
                }
                return true;
            }),$"{transition}: teardown failed");
        return true;
    }

    private static bool DensityGuards()
    {
        Require(DensityRename(true),"invalid draft width trace failed");
        return WithShell(shell =>
        {
            _ = shell.PrepareTaskManagerForSnapshot();
            _ = shell.ExplorerItemList.Focus();
            _ = shell.SelectLayoutForTest(LayoutPreset.WideTopNarrowBottom); Pump();
            ExplorerSurface owner = shell.GetExplorerSurfaceForTest("explorer");
            owner.ExplorerItemList.SelectedIndex = 0;
            ExplorerWorkspaceScenarios.RaiseKey(owner.ExplorerItemList,Key.F2); Pump();
            TextBox editor = (TextBox)Keyboard.FocusedElement!; editor.Text = "BAD/NAME";
            ExplorerWorkspaceScenarios.RaiseKey(editor,Key.Enter); Pump(); editor.Select(2,3);
            string error = owner.ExplorerRenameErrorText.Text;
            PartitionDivider divider = (PartitionDivider)Dividers(shell).Single();
            BeginDrag(divider,new Point(600,525));
            foreach (double y in new[] { 350d,212d,525d })
            {
                DragMove(divider,new Point(600,y)); shell.UpdateLayout(); Pump();
                Require(ReferenceEquals(Keyboard.FocusedElement,editor) && editor.Text == "BAD/NAME" && editor.SelectionStart == 2 && editor.SelectionLength == 3 &&
                    owner.ExplorerRenameErrorText.Text == error && owner.Workspace.RenameItemId is not null,"height crossing lost draft/error/caret/focus");
            }
            divider.ReleaseMouseCapture();
            _ = shell.TaskbarSearchButton.Focus(); Pump();
            _ = shell.SelectLayoutForTest(LayoutPreset.WideLeftNarrowRight); Pump();
            PartitionDivider width = (PartitionDivider)Dividers(shell).Single();
            width.SetRatio(0.5); shell.UpdateLayout(); Pump();
            TextBox retained = ExplorerWorkspaceScenarios.Descendants(owner).OfType<TextBox>().Single(item => item.IsVisible);
            Require(shell.TaskbarSearchButton.IsKeyboardFocused && retained.Text == "BAD/NAME" && owner.ExplorerRenameErrorText.Text == error,"density stole newer Search focus or error draft");
            string secondaryId = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell); Pump();
            _ = shell.GetExplorerSurfaceForTest(secondaryId).ExplorerItemList.Focus(); Pump();
            _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns); Pump();
            ((PartitionDivider)Dividers(shell).Single()).SetRatio(0.6); Pump();
            Require(shell.GetExplorerSurfaceForTest(secondaryId).IsKeyboardFocusWithin,"density stole newer foreign-session focus");
            return true;
        });
    }

    private static bool HostDeactivation() => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns); Pump();
        PartitionDivider divider = (PartitionDivider)Dividers(shell).Single();
        BeginDrag(divider,new Point(862,300)); DragMove(divider,new Point(900,300));
        typeof(DesktopShellSurface).GetMethod("HostWindow_OnDeactivated",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
            .Invoke(shell,new object?[] { Window.GetWindow(shell),EventArgs.Empty });
        Require(Mouse.Captured is null && !divider.HasGesture,"owning host deactivation left capture/intent live");
        string current = Fingerprint(shell);
        DragMove(divider,new Point(1000,300));
        Require(Fingerprint(shell) == current && shell.GetOwnedWindowBoundsForTest("explorer").Width == 752,"deactivation stale movement or rollback");
        return true;
    });

    private static bool GestureEscape() => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns); Pump();
        ExplorerSurface explorer = shell.GetExplorerSurfaceForTest("explorer");
        explorer.ExplorerItemList.SelectedIndex = 0;
        _ = explorer.ExplorerItemList.Focus();
        ExplorerWorkspaceScenarios.RaiseKey((UIElement)Keyboard.FocusedElement!,Key.F2); Pump();
        TextBox editor = (TextBox)Keyboard.FocusedElement!;
        editor.Text = "ESCAPE_MUST_NOT_CANCEL_RENAME";
        string? rename = explorer.Workspace.RenameItemId;
        PartitionGeometry start = Coordinator(shell).Partition!;
        PartitionDivider divider = (PartitionDivider)Dividers(shell).Single();
        BeginDrag(divider,new Point(862,300));
        DragMove(divider,new Point(920,300));
        Require(shell.GetOwnedWindowBoundsForTest("explorer").Width == 772,"Escape fixture did not resize");
        KeyEventArgs escape = new(Keyboard.PrimaryDevice,PresentationSource.FromVisual(shell),Environment.TickCount,Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        ((UIElement)Keyboard.FocusedElement!).RaiseEvent(escape);
        Require(escape.Handled && Mouse.Captured is null && Coordinator(shell).Partition!.Panes.SequenceEqual(start.Panes) &&
            explorer.Workspace.RenameItemId == rename && ((TextBox)Keyboard.FocusedElement!).Text == "ESCAPE_MUST_NOT_CANCEL_RENAME",
            "pointer Escape not consumed/rolled back or canceled content rename");
        BeginDrag(divider,new Point(862,300)); DragMove(divider,new Point(900,300));
        _ = shell.TaskbarSearchButton.Focus();
        divider.ReleaseMouseCapture();
        Require(shell.GetOwnedWindowBoundsForTest("explorer").Width == 752,"ordinary loss rolled geometry back");
        BeginDrag(divider,new Point(900,300)); DragMove(divider,new Point(950,300));
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualRows); Pump();
        string current = Fingerprint(shell);
        DragMove(divider,new Point(1000,300));
        divider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        divider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount) { RoutedEvent = Mouse.LostMouseCaptureEvent });
        Require(Mouse.Captured is null && Fingerprint(shell) == current && !divider.IsCurrent,"stale events mutated newer template");
        return true;
    });

    internal static void BeginDrag(PartitionDivider divider, Point point)
    {
        SetPointer(divider,point,MouseButtonState.Pressed);
        divider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        Require(ReferenceEquals(Mouse.Captured,divider),"actual WPF capture was not established");
    }
    internal static void DragMove(PartitionDivider divider, Point point)
    {
        SetPointer(divider,point,MouseButtonState.Pressed);
        divider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount) { RoutedEvent = Mouse.MouseMoveEvent });
    }

    private static bool DensityRename(bool invalid) => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.WideLeftNarrowRight); Pump(); shell.UpdateLayout();
        ExplorerSurface owner = shell.GetExplorerSurfaceForTest("explorer");
        owner.ExplorerItemList.SelectedIndex = 0;
        _ = owner.ExplorerItemList.Focus();
        ExplorerWorkspaceScenarios.RaiseKey((UIElement)Keyboard.FocusedElement!,Key.F2); Pump();
        TextBox editor = ExplorerWorkspaceScenarios.Descendants(owner).OfType<TextBox>().Single(item => item.IsVisible);
        string renameId = owner.Workspace.RenameItemId!;
        string originalName = owner.Workspace.Items.Single(item => item.Id == renameId).Name;
        string draft = invalid ? "BAD/NAME" : "UNCOMMITTED_DENSITY_DRAFT.DOCX";
        editor.Text = draft; editor.Select(4,3);
        Require(editor.IsKeyboardFocused,"rename must be actually focused before density crossing");
        PartitionDivider divider = (PartitionDivider)Dividers(shell).Single();
        Point anchor = new(1100,300);
        SetPointer(divider,anchor,MouseButtonState.Pressed);
        divider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        foreach (double pointer in new[] { 900d,468d,1100d })
        {
            SetPointer(divider,new Point(pointer,300),MouseButtonState.Pressed);
            divider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount) { RoutedEvent = Mouse.MouseMoveEvent });
            shell.UpdateLayout(); Pump();
            TextBox? current = ExplorerWorkspaceScenarios.Descendants(owner).OfType<TextBox>().FirstOrDefault(item => item.IsVisible);
            Require(owner.Workspace.RenameItemId == renameId && owner.Workspace.Items.Single(item => item.Id == renameId).Name == originalName &&
                current is not null && current.Text == draft && current.SelectionStart == 4 && current.SelectionLength == 3 && current.IsKeyboardFocused,
                $"density destroyed/committed draft: rename={owner.Workspace.RenameItemId}, text={current?.Text}, focus={Keyboard.FocusedElement?.GetType().Name}, ownerFocused={owner.IsKeyboardFocusWithin}");
            Require(current!.ActualWidth >= 28 && shell.GetOwnedWindowBoundsForTest("explorer").Contains(
                current.TransformToAncestor(shell).TransformBounds(new Rect(current.RenderSize))),"retained editor unusable at 320-DIP clamp");
        }
        SetPointer(divider,anchor,MouseButtonState.Released);
        divider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        SetPointer(divider,null,null);
        return Mouse.Captured is null;
    });

    private static bool CapturedDrag() => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns); Pump(); shell.UpdateLayout();
        PartitionDivider divider = (PartitionDivider)Dividers(shell).Single();
        _ = shell.TaskbarSearchButton.Focus();
        string? active = shell.ActiveOwnedWindowId;
        OwnedWindowState[] before = Coordinator(shell).CaptureSnapshot().Windows.ToArray();
        Point anchor = new(862,300);
        SetPointer(divider,anchor,MouseButtonState.Pressed);
        divider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        Require(ReferenceEquals(Mouse.Captured,divider) && shell.TaskbarSearchButton.IsKeyboardFocused,"divider did not own real capture or stole editor/Search focus");
        foreach ((double pointer,double expectedWidth) in new[] { (882d,734d),(902d,754d),(902d,754d),(5000d,1108d),(900d,752d),(0d,320d),(862d,714d) })
        {
            SetPointer(divider,new Point(pointer,300),MouseButtonState.Pressed);
            divider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount) { RoutedEvent = Mouse.MouseMoveEvent });
            shell.UpdateLayout();
            Require(shell.GetOwnedWindowBoundsForTest("explorer").Width == expectedWidth && ReferenceEquals(Mouse.Captured,divider) &&
                ReferenceEquals(Dividers(shell).Single(),divider),$"absolute drag additive/clamp/reverse error at {pointer}: width={shell.GetOwnedWindowBoundsForTest("explorer").Width}");
        }
        SetPointer(divider,anchor,MouseButtonState.Released);
        divider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        SetPointer(divider,null,null);
        BeginDrag(divider,new Point(-double.MaxValue,300));
        string overflowBefore = Fingerprint(shell);
        DragMove(divider,new Point(double.MaxValue,300));
        Require(divider.HasGesture && ReferenceEquals(Mouse.Captured,divider) && overflowBefore == Fingerprint(shell),
            "finite pointer subtraction overflow changed state or canceled capture");
        divider.ReleaseMouseCapture(); SetPointer(divider,null,null);
        Require(Mouse.Captured is null && shell.TaskbarSearchButton.IsKeyboardFocused && shell.ActiveOwnedWindowId == active &&
            before.All(original => Coordinator(shell).CaptureSnapshot().Windows.Any(current => current.Id == original.Id &&
                current.ActivationOrdinal == original.ActivationOrdinal && current.FloatingBounds == original.FloatingBounds && current.AssignedPaneIndex == original.AssignedPaneIndex)),
            "drag release leaked capture/changed owner/history/floating");
        return true;
    });

    internal static void SetPointer(PartitionDivider divider, Point? point, MouseButtonState? button)
    {
        var flags = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(PartitionDivider).GetField("_pointerOverrideForTest",flags)?.SetValue(divider,point);
        typeof(PartitionDivider).GetField("_buttonOverrideForTest",flags)?.SetValue(divider,button);
    }

    private static bool ZoomAndRatios() => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.FourQuadrants); Pump();
        foreach (PartitionDivider divider in Dividers(shell).Cast<PartitionDivider>())
            divider.SetRatio(divider.Key.Axis == PartitionAxis.Vertical ? 0.6 : 0.55);
        _ = shell.TaskbarSearchButton.Focus();
        OwnedWindowLayoutCoordinator coordinator = Coordinator(shell);
        OwnedWindowLayoutSnapshot before = coordinator.CaptureSnapshot();
        string[] ids = shell.ExplorerSessionIdsForTest.ToArray();
        shell.ExplorerMaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        shell.UpdateLayout(); Pump();
        Require(shell.IsExplorerMaximized && shell.VisibleOwnedWindowCount == 1 && shell.ExplorerWindow.IsVisible &&
            shell.GetOwnedWindowBoundsForTest("explorer") == DesktopShellSurface.OwnedWorkArea && Dividers(shell).Length == 0,
            "Partition maximize is not a temporary single-window zoom");
        Require(ids.SequenceEqual(shell.ExplorerSessionIdsForTest) && shell.TaskbarSearchButton.IsKeyboardFocused,"zoom created/focused another window");
        shell.ExplorerMaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        shell.UpdateLayout(); Pump();
        Require(shell.CurrentLayout == LayoutPreset.FourQuadrants && shell.VisibleOwnedWindowCount == 4 &&
            before.Windows.Select(state => (state.Id,state.Bounds,state.FloatingBounds,state.AssignedPaneIndex,state.ActivationOrdinal))
            .SequenceEqual(coordinator.CaptureSnapshot().Windows.Select(state => (state.Id,state.Bounds,state.FloatingBounds,state.AssignedPaneIndex,state.ActivationOrdinal))),
            "zoom restore replanned MRU/default cuts instead of exact saved composition");
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualRows); Pump();
        _ = shell.SelectLayoutForTest(LayoutPreset.FourQuadrants); Pump();
        PartitionGeometry geometry = coordinator.Partition!;
        Require(geometry.Panes.SequenceEqual(before.Windows.Where(state => state.AssignedPaneIndex.HasValue).OrderBy(state => state.AssignedPaneIndex).Select(state => state.Bounds)),
            "switching back discarded per-template ratios");
        Require(shell.ExplorerSessionIdsForTest.SequenceEqual(ids),"zoom/cache allocated deficit");
        return true;
    });

    private static bool AccessibleResize() => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
        Pump(); shell.UpdateLayout();
        OwnedWindowLayoutCoordinator coordinator = Coordinator(shell);
        OwnedWindowState[] before = coordinator.CaptureSnapshot().Windows.ToArray();
        FrameworkElement[] dividers = Dividers(shell);
        Require(dividers.Length == 1,"shared divider control is absent");
        FrameworkElement divider = dividers[0];
        System.Windows.Automation.Peers.AutomationPeer? peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(divider);
        Require(peer is not null && peer.GetName().Contains("Vertical",StringComparison.Ordinal) &&
            peer.GetOrientation() == System.Windows.Automation.Peers.AutomationOrientation.Vertical,
            "real divider peer/name/orientation absent");
        var range = peer!.GetPattern(System.Windows.Automation.Peers.PatternInterface.RangeValue) as System.Windows.Automation.Provider.IRangeValueProvider;
        Require(range is not null && !range.IsReadOnly && range.Minimum > 0 && range.Maximum < 1,
            $"range resizing provider absent: range={range is not null}, loaded={divider.IsLoaded}, visible={divider.IsVisible}, readOnly={range?.IsReadOnly}, epoch={coordinator.PartitionGeneration}");
        _ = shell.TaskbarSearchButton.Focus();
        string? active = shell.ActiveOwnedWindowId;
        range!.SetValue(0.6);
        shell.UpdateLayout();
        Rect left = shell.GetOwnedWindowBoundsForTest("explorer");
        Require(left.Width == 857 && shell.TaskbarSearchButton.IsKeyboardFocused && shell.ActiveOwnedWindowId == active,"peer did not route real atomic pane resize or stole focus");
        Require(divider.Focus() && ReferenceEquals(Keyboard.FocusedElement,divider),"divider is not reachable for arrow operation");
        ExplorerWorkspaceScenarios.RaiseKey((UIElement)Keyboard.FocusedElement!,Key.Left);
        shell.UpdateLayout();
        Require(shell.GetOwnedWindowBoundsForTest("explorer").Width == left.Width-8 && shell.ActiveOwnedWindowId == active,"focused arrow did not use 8-DIP step or activated content");
        foreach (double value in new[] { double.NaN,double.PositiveInfinity,range.Minimum-0.01,range.Maximum+0.01 })
        {
            string fingerprint = Fingerprint(shell);
            try { range.SetValue(value); throw new InvalidOperationException("invalid automation value accepted"); }
            catch (ArgumentOutOfRangeException) { }
            Require(fingerprint == Fingerprint(shell),"invalid peer request mutated state");
        }
        range.SetValue(range.Minimum); shell.UpdateLayout();
        Require(shell.GetOwnedWindowBoundsForTest("explorer").Width == 320,"automation minimum is not snapped legal edge");
        Require(before.All(original => coordinator.CaptureSnapshot().Windows.Any(current => original.Id == current.Id &&
            original.FloatingBounds == current.FloatingBounds && original.ActivationOrdinal == current.ActivationOrdinal &&
            original.AssignedPaneIndex == current.AssignedPaneIndex)),"resize changed assignment/floating/history");
        return true;
    });

    internal static FrameworkElement[] Dividers(DesktopShellSurface shell) => shell.DesktopCanvas.Children.OfType<FrameworkElement>()
        .Where(element => element.GetType().Name == "PartitionDivider").ToArray();

    private static bool LifecycleRefill()
    {
        foreach (string id in new[] { "explorer","task-manager" })
            foreach (string action in new[] { "Minimize","Close" })
                Require(WithShell(shell =>
                {
                    _ = shell.SelectLayoutForTest(LayoutPreset.FourQuadrants);
                    OwnedWindowLayoutCoordinator coordinator = Coordinator(shell);
                    OwnedWindowState[] before = coordinator.CaptureSnapshot().Windows.ToArray();
                    Rect vacancy = shell.GetOwnedWindowBoundsForTest(id);
                    Button command = id == "task-manager" ? action == "Minimize" ? shell.TaskManagerMinimizeButton : shell.TaskManagerCloseButton
                        : OwnedWindowLayoutScenarios.ExplorerTitleButton(shell,id,action);
                    command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    shell.UpdateLayout();
                    OwnedWindowState[] after = coordinator.CaptureSnapshot().Windows.ToArray();
                    OwnedWindowState target = after.Single(state => state.Id.Value == id);
                    Require(target.Lifecycle == (action == "Close" ? OwnedWindowLifecycle.Closed : OwnedWindowLifecycle.Minimized) &&
                        target.AssignedPaneIndex is null && shell.VisibleOwnedWindowCount == 4 && shell.CurrentLayout == LayoutPreset.FourQuadrants &&
                        after.Single(state => state.Lifecycle == OwnedWindowLifecycle.Visible && state.Bounds == vacancy).Id.Value != id,
                        $"{id}/{action}: hole, immediate target resurrection or lost layout");
                    Require(before.Where(state => state.Id.Value != id).All(original => after.Any(current => current.Id == original.Id &&
                        current.Bounds == original.Bounds && current.FloatingBounds == original.FloatingBounds && current.ActivationOrdinal == original.ActivationOrdinal)) &&
                        after.Length == 5 && shell.ExplorerSessionIdsForTest.Count == 4,"refill moved another pane/history or allocated wrong deficit");
                    string menuAction = action == "Close" ? "Reopen" : "Restore";
                    if (id == "task-manager") shell.TaskbarSystemButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else OwnedWindowLayoutScenarios.InvokeSessionMenu(shell,id,menuAction);
                    Pump();
                    Require(shell.GetOwnedWindowBoundsForTest(id) == vacancy && shell.VisibleOwnedWindowCount == 4,"restore did not swap into compatible former pane");
                    Exit(shell); Pump();
                    return shell.IsOwnedWindowLayoutFreeform;
                }), $"{id}/{action} trace failed");
        DesktopShellSurface failing = new(number => throw new InvalidOperationException("injected replacement failure"));
        return WithShell(shell =>
        {
            _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
            string before = Fingerprint(shell);
            UIElement[] children = shell.DesktopCanvas.Children.Cast<UIElement>().ToArray();
            shell.ExplorerMinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(before == Fingerprint(shell) && shell.ExplorerWindow.IsVisible && shell.VisibleOwnedWindowCount == 2 &&
                children.SequenceEqual(shell.DesktopCanvas.Children.Cast<UIElement>()) &&
                shell.InteractionStatusForTest.Contains("FAILED",StringComparison.Ordinal),"failed refill partially hid target or lied about success");
            return true;
        },failing);
    }

    private static bool MembershipSwaps() => WithShell(shell =>
    {
        string second = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        ExplorerSurface primary = shell.GetExplorerSurfaceForTest("explorer");
        ExplorerSurface secondary = shell.GetExplorerSurfaceForTest(second);
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
        Rect originalPane = shell.GetOwnedWindowBoundsForTest("explorer");
        Rect secondPane = shell.GetOwnedWindowBoundsForTest(second);
        _ = shell.SelectLayoutForTest(LayoutPreset.Maximized);
        Require(!primary.IsVisible && secondary.IsVisible, "swap fixture did not suppress primary");
        OwnedWindowLayoutScenarios.InvokeSessionMenu(shell,"explorer","Show");
        Pump();
        Require(primary.IsVisible && !secondary.IsVisible && shell.VisibleOwnedWindowCount == 1 &&
            shell.GetOwnedWindowBoundsForTest("explorer") == DesktopShellSurface.OwnedWorkArea && shell.ActiveOwnedWindowId == "explorer",
            "suppressed taskbar identity floated instead of swapping");
        string fresh = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        Pump();
        Require(fresh == "explorer-3" && !primary.IsVisible && !secondary.IsVisible && shell.GetExplorerSurfaceForTest(fresh).IsVisible &&
            shell.VisibleOwnedWindowCount == 1 && shell.GetOwnedWindowBoundsForTest(fresh) == DesktopShellSurface.OwnedWorkArea,
            "explicit New floated above partition");
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
        Rect[] composition = Coordinator(shell).CaptureSnapshot().Windows.Where(state => state.Lifecycle == OwnedWindowLifecycle.Visible).Select(state => state.Bounds).ToArray();
        ExactCover(composition,DesktopShellSurface.OwnedWorkArea);
        OwnedWindowState[] beforeActivation = Coordinator(shell).CaptureSnapshot().Windows.ToArray();
        string active = shell.ActiveOwnedWindowId!;
        OwnedWindowLayoutScenarios.InvokeSessionMenu(shell,active,"Activate");
        Require(beforeActivation.Select(state => (state.Id,state.Bounds,state.AssignedPaneIndex)).SequenceEqual(
            Coordinator(shell).CaptureSnapshot().Windows.Select(state => (state.Id,state.Bounds,state.AssignedPaneIndex))), "assigned activation reshuffled");
        string fourth = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        Require(fourth == "explorer-4" && shell.VisibleOwnedWindowCount == 2, "New cap or membership wrong");
        string beforeCap = Fingerprint(shell);
        MenuItem create = (MenuItem)shell.TaskbarExplorerButton.ContextMenu.Items[0];
        Require(!create.IsEnabled, "New not disabled at four retained Explorers");
        create.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(Fingerprint(shell) == beforeCap && shell.ExplorerSessionIdsForTest.Count == 4 &&
            ReferenceEquals(primary,shell.GetExplorerSurfaceForTest("explorer")) && ReferenceEquals(secondary,shell.GetExplorerSurfaceForTest(second)),
            "inert cap recycled/replaced an identity");
        return true;
    });

    private static bool TransactionalPreparation()
    {
        System.Reflection.ConstructorInfo? constructor = typeof(DesktopShellSurface).GetConstructor(
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[] { typeof(Func<int,ExplorerSurface>) },null);
        Require(constructor is not null,"detached batch factory failure seam unavailable");
        int prepared = 0;
        Func<int,ExplorerSurface> factory = number =>
        {
            prepared++;
            if (number == 3) throw new InvalidOperationException("injected later allocation failure");
            return new ExplorerSurface();
        };
        return WithShell(shell =>
        {
            OwnedWindowLayoutCoordinator coordinator = Coordinator(shell);
            string before = Fingerprint(shell);
            UIElement[] children = shell.DesktopCanvas.Children.Cast<UIElement>().ToArray();
            shell.PreviewLayoutForTest(LayoutPreset.FourQuadrants);
            shell.RaiseRailKeyForTest(Key.Enter);
            Require(prepared == 2 && before == Fingerprint(shell) &&
                children.SequenceEqual(shell.DesktopCanvas.Children.Cast<UIElement>()) && shell.IsOwnedWindowLayoutFreeform &&
                shell.InteractionStatusForTest.Contains("FAILED",StringComparison.Ordinal), "later factory failure partially published/counted/changed visuals");
            foreach (Rect bad in new[] { new Rect(1e308,24,1e308,752),new Rect(0,24,5e307,752),new Rect(148,24,319,187),new Rect(148.1,24,1428,752) })
            {
                OwnedLayoutApplyResult result = coordinator.TryCommitPartition(LayoutPreset.FourQuadrants,bad,
                    new[] { new OwnedWindowAdmission(new("explorer-2"),new Rect(568,81,930,667)),new OwnedWindowAdmission(new("explorer-3"),new Rect(540,99,930,667)) });
                Require(!result.Succeeded && before == Fingerprint(shell),"geometry rejection changed model/session map/counters");
            }
            OwnedLayoutApplyResult duplicate = coordinator.TryCommitPartition(LayoutPreset.FourQuadrants,DesktopShellSurface.OwnedWorkArea,
                new[] { new OwnedWindowAdmission(new("explorer-2"),new Rect(568,81,930,667)),new OwnedWindowAdmission(new("explorer-2"),new Rect(540,99,930,667)) });
            Require(!duplicate.Succeeded && before == Fingerprint(shell),"duplicate batch was partially admitted");
            return true;
        },(DesktopShellSurface)constructor!.Invoke(new object[] { factory }));
    }

    internal static string Fingerprint(DesktopShellSurface shell)
    {
        OwnedWindowLayoutCoordinator coordinator = Coordinator(shell);
        object? Field(string name) => typeof(OwnedWindowLayoutCoordinator).GetField(name,
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(coordinator);
        var cache = (Dictionary<LayoutPreset,IReadOnlyDictionary<PartitionCutKey,double>>)Field("_partitionRatios")!;
        var remembered = (Dictionary<OwnedWindowId,(LayoutPreset Preset,int Pane)>)Field("_previousPartitionPane")!;
        var frames = shell.ExplorerSessionIdsForTest.Select(id => OwnedWindowLayoutScenarios.ExplorerFrame(shell,id))
            .Cast<FrameworkElement>().Append(shell.TaskManagerWindow).Select(frame => new
            {
                Identity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(frame), frame.Visibility,
                Left = Canvas.GetLeft(frame), Top = Canvas.GetTop(frame), frame.Width, frame.Height,
                Z = Panel.GetZIndex(frame),
            });
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            State = coordinator.CaptureSnapshot(), Activation = Field("_activationOrdinal"), Registration = Field("_registrationOrder"),
            Epoch = Field("_partitionGeneration"), Zoom = Field("_partitionZoom"), Assignments = Field("_partitionAssignments"),
            Committed = Field("_committedPaneBounds"), coordinator.IsPartition,
            Geometry = coordinator.Partition is PartitionGeometry geometry ? new
                { geometry.Preset, geometry.WorkArea, geometry.Panes, geometry.Dividers } : null,
            Ratios = cache.OrderBy(pair => pair.Key).Select(pair => new { Preset = pair.Key,
                Cuts = pair.Value.OrderBy(cut => cut.Key.Axis).ThenBy(cut => cut.Key.GridCoordinate).Select(cut => new { cut.Key, cut.Value }) }),
            Remembered = remembered.OrderBy(pair => pair.Key.Value).Select(pair => new { Id = pair.Key, pair.Value.Preset, pair.Value.Pane }),
            Sessions = shell.ExplorerSessionIdsForTest, Frames = frames,
            Children = shell.DesktopCanvas.Children.Cast<UIElement>().Select(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode),
        });
    }

    private static bool ExitFreeform() => WithShell(shell =>
    {
        string secondaryId = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        ExplorerSurface secondary = shell.GetExplorerSurfaceForTest(secondaryId);
        Rect primaryFloating = shell.GetOwnedWindowBoundsForTest("explorer");
        Rect secondaryFloating = shell.GetOwnedWindowBoundsForTest(secondaryId);
        _ = shell.SelectLayoutForTest(LayoutPreset.Maximized);
        Require(!shell.ExplorerWindow.IsVisible && secondary.IsVisible, "surplus fixture not suppressed");
        ContextMenu? menu = shell.CurrentLayoutButton.ContextMenu;
        Require(menu is not null, "discoverable Freeform context command absent");
        MenuItem? command = menu!.Items.OfType<MenuItem>().SingleOrDefault(item => item.CommandParameter is string value && value == "freeform");
        Require(command is not null && command.Header is string header && header.Contains("_Freeform",StringComparison.Ordinal) &&
            AutomationProperties.GetName(command) == "Freeform" &&
            AutomationProperties.GetHelpText(shell.CurrentLayoutButton).Contains("Shift+F10",StringComparison.Ordinal), "Freeform access/help/name missing");
        _ = shell.CurrentLayoutButton.Focus();
        ExplorerWorkspaceScenarios.RaiseKey(shell.CurrentLayoutButton,Key.Apps);
        Pump(); Require(menu.IsOpen, "Apps does not open current-layout commands");
        command!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Pump(); shell.UpdateLayout();
        Require(shell.IsOwnedWindowLayoutFreeform && shell.CurrentLayoutGlyph.IsFreeform &&
            shell.ExplorerWindow.IsVisible && secondary.IsVisible &&
            shell.GetOwnedWindowBoundsForTest("explorer") == primaryFloating && shell.GetOwnedWindowBoundsForTest(secondaryId) == secondaryFloating &&
            !shell.IsTaskManagerVisibleForTest && !shell.IsTaskManagerSamplingForTest &&
            ReferenceEquals(secondary,shell.GetExplorerSurfaceForTest(secondaryId)), "exit lost floating/retained or reopened explicit Closed identity");
        return true;
    });

    internal static void Exit(DesktopShellSurface shell)
    {
        MenuItem command = shell.CurrentLayoutButton.ContextMenu.Items.OfType<MenuItem>().Single(item => item.CommandParameter is string value && value == "freeform");
        command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static bool LockedTitles() => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
        foreach (string id in new[] { "explorer", "task-manager" })
        {
            Rect pane = shell.GetOwnedWindowBoundsForTest(id);
            Point press = new(pane.Left+40,pane.Top+20);
            foreach (bool loss in new[] { false,true })
            {
                shell.RaiseOwnedTitleGestureForTest(id,press,new Point(press.X+70,press.Y+50),loss);
                Pump();
                Require(shell.CurrentLayout == LayoutPreset.EqualColumns && shell.GetOwnedWindowBoundsForTest(id) == pane && Mouse.Captured is null,
                    $"{id}: enforced title escaped/moved or leaked capture");
            }
            shell.MoveOwnedWindowForTest(id,new Rect(420,90,700,600));
            Require(shell.GetOwnedWindowBoundsForTest(id) == pane, $"{id}: WPF manual seam mutated before model rejected");
        }
        OwnedWindowLayoutCoordinator coordinator = Coordinator(shell);
        string before = System.Text.Json.JsonSerializer.Serialize(coordinator.CaptureSnapshot());
        Require(!coordinator.TrySetManualBounds(new("explorer"),new Rect(200,40,400,400)) &&
            !coordinator.TrySetLifecycle(new("explorer"),OwnedWindowLifecycle.Closed) &&
            !coordinator.TryRegister(new("intruder"),OwnedWindowLifecycle.Visible,new Rect(200,40,400,400)) &&
            !coordinator.TryApply(LayoutPreset.EqualRows,DesktopShellSurface.OwnedWorkArea).Succeeded &&
            !coordinator.TryToggleMaximize(new("explorer"),DesktopShellSurface.OwnedWorkArea).Succeeded &&
            before == System.Text.Json.JsonSerializer.Serialize(coordinator.CaptureSnapshot()), "legacy mutation broke enforced mode");
        return true;
    });

    internal static OwnedWindowLayoutCoordinator Coordinator(DesktopShellSurface shell) =>
        (OwnedWindowLayoutCoordinator)typeof(DesktopShellSurface).GetField("_ownedWindowLayouts",
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(shell)!;

    internal static void ExactCover(IReadOnlyList<Rect> panes, Rect area)
    {
        Require(panes.All(p => p.Width >= 320 && p.Height >= 188 && area.Contains(p) &&
            p.Left == Math.Round(p.Left) && p.Top == Math.Round(p.Top) && p.Right == Math.Round(p.Right) && p.Bottom == Math.Round(p.Bottom)), "pane minimum/integer/bounds");
        double[] xs = panes.SelectMany(p => new[] { p.Left, p.Right }).Append(area.Left).Append(area.Right).Distinct().Order().ToArray();
        double[] ys = panes.SelectMany(p => new[] { p.Top, p.Bottom }).Append(area.Top).Append(area.Bottom).Distinct().Order().ToArray();
        for (int x = 0; x < xs.Length - 1; x++)
            for (int y = 0; y < ys.Length - 1; y++)
                Require(panes.Count(p => p.Left <= xs[x] && p.Right >= xs[x + 1] && p.Top <= ys[y] && p.Bottom >= ys[y + 1]) == 1,
                    $"hole/overlap at {xs[x]},{ys[y]}..{xs[x+1]},{ys[y+1]}");
        for (int first = 0; first < panes.Count; first++)
            for (int second = first + 1; second < panes.Count; second++)
                Require(!(Math.Max(panes[first].Left, panes[second].Left) < Math.Min(panes[first].Right, panes[second].Right) &&
                    Math.Max(panes[first].Top, panes[second].Top) < Math.Min(panes[first].Bottom, panes[second].Bottom)), "positive area overlap");
    }

    internal static bool WithShell(Func<DesktopShellSurface, bool> action, DesktopShellSurface? supplied = null)
    {
        DesktopShellSurface shell = supplied ?? new();
        Window host = new()
        {
            Width = 1600, Height = 900, Left = -32000, Top = -32000,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Background = Brushes.Black, Content = shell,
        };
        try
        {
            host.Show(); Pump(); shell.UpdateLayout(); shell.PrepareLayoutRailForSnapshot();
            return action(shell);
        }
        finally { shell.TaskManagerPanel.StopSampling(); host.Content = null; host.Close(); Pump(); }
    }

    internal static void Pump()
    {
        DispatcherFrame frame = new();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Add(ICollection<OwnedWindowLayoutScenario> scenarios, string name, Func<bool> action)
    {
        try { bool passed = action(); scenarios.Add(new(name, passed, passed ? "passed" : "predicate-returned-false")); }
        catch (Exception exception) { scenarios.Add(new(name, false, $"{exception.GetType().Name}: {exception.Message}")); }
    }
}
