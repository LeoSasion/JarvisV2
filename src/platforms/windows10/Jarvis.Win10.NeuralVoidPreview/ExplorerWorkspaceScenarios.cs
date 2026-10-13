using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Jarvis.Win10.NeuralVoidPreview;

internal sealed record ExplorerWorkspaceScenario(
    string Name,
    bool Passed,
    string Detail);

internal sealed record ExplorerWorkspaceTestReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    bool OwnProcessOnly,
    bool OperatingSystemLaunchSupported,
    bool RealFileSystemAccessSupported,
    IReadOnlyList<ExplorerWorkspaceScenario> Scenarios);

internal static class ExplorerWorkspaceScenarios
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static ExplorerWorkspaceTestReceipt Run()
    {
        List<ExplorerWorkspaceScenario> scenarios = [];

        Add(
            scenarios,
            "stable-virtual-tree-and-documents-seed",
            () =>
            {
                ExplorerWorkspace first = new();
                ExplorerWorkspace second = new();
                string[] expectedBreadcrumb =
                [
                    ExplorerWorkspace.ThisPcId,
                    ExplorerWorkspace.NeuralStorageId,
                    ExplorerWorkspace.DocumentsId,
                ];
                string firstSignature = Signature(first.CaptureSnapshot());
                string secondSignature = Signature(second.CaptureSnapshot());
                bool initialDocuments =
                    first.CurrentLocationId == ExplorerWorkspace.DocumentsId &&
                    first.Breadcrumb.Select(segment => segment.Id)
                        .SequenceEqual(expectedBreadcrumb) &&
                    first.Items.Count == 6 &&
                    first.Items[0].Id == ExplorerWorkspace.ProjectBriefId &&
                    first.Items[1].Id == ExplorerWorkspace.SpecificationsId;
                bool stableLocations = first.TryGoUp() &&
                    first.Items.Select(item => item.Id).SequenceEqual(
                        new[]
                        {
                            ExplorerWorkspace.DocumentsId,
                            ExplorerWorkspace.ProjectsId,
                            ExplorerWorkspace.ArchiveId,
                        });
                return initialDocuments &&
                    stableLocations &&
                    firstSignature == secondSignature;
            });

        Add(
            scenarios,
            "breadcrumb-up-back-forward-and-branch-history",
            () =>
            {
                ExplorerWorkspace workspace = new();
                bool up = workspace.TryGoUp() &&
                    workspace.CurrentLocationId ==
                        ExplorerWorkspace.NeuralStorageId &&
                    workspace.CanGoBack &&
                    workspace.CanGoUp;
                bool back = workspace.TryGoBack() &&
                    workspace.CurrentLocationId == ExplorerWorkspace.DocumentsId &&
                    workspace.CanGoForward;
                bool forward = workspace.TryGoForward() &&
                    workspace.CurrentLocationId ==
                        ExplorerWorkspace.NeuralStorageId;
                bool branch = workspace.TryNavigateTo(
                        ExplorerWorkspace.ArchiveId) &&
                    !workspace.CanGoForward &&
                    workspace.Breadcrumb.Select(segment => segment.Id)
                        .SequenceEqual(
                            new[]
                            {
                                ExplorerWorkspace.ThisPcId,
                                ExplorerWorkspace.NeuralStorageId,
                                ExplorerWorkspace.ArchiveId,
                            });
                bool rootBoundary = workspace.TryNavigateTo(
                        ExplorerWorkspace.ThisPcId) &&
                    !workspace.TryGoUp();
                return up && back && forward && branch && rootBoundary;
            });

        Add(
            scenarios,
            "navigation-cleans-selection-and-rename-session",
            () =>
            {
                ExplorerWorkspace workspace = new();
                bool selected = workspace.SelectItem(
                    ExplorerWorkspace.ProjectBriefId);
                ExplorerRenameResult rename =
                    workspace.BeginRenameSelected();
                bool navigated = workspace.TryGoUp();
                ExplorerWorkspaceSnapshot snapshot =
                    workspace.CaptureSnapshot();
                return selected &&
                    rename.Succeeded &&
                    navigated &&
                    snapshot.SelectedItemId is null &&
                    snapshot.RenameItemId is null &&
                    snapshot.Items.All(item =>
                        !item.IsSelected && !item.IsRenameTarget);
            });

        Add(
            scenarios,
            "invalid-selection-cleans-stale-interaction-state",
            () =>
            {
                ExplorerWorkspace workspace = new();
                _ = workspace.SelectItem(ExplorerWorkspace.ProjectBriefId);
                _ = workspace.BeginRenameSelected();
                bool accepted = workspace.SelectItem("file:not-present");
                return !accepted &&
                    workspace.SelectedItemId is null &&
                    workspace.RenameItemId is null;
            });

        Add(
            scenarios,
            "open-folder-navigates-inside-virtual-tree",
            () =>
            {
                ExplorerWorkspace workspace = new();
                _ = workspace.TryGoUp();
                ExplorerOpenResult result = workspace.OpenItem(
                    ExplorerWorkspace.ProjectsId);
                return result.Disposition ==
                        ExplorerOpenDisposition.Navigated &&
                    result.PreviewRequest is null &&
                    workspace.CurrentLocationId ==
                        ExplorerWorkspace.ProjectsId &&
                    workspace.SelectedItemId is null &&
                    workspace.Breadcrumb[^1].Name == "PROJECTS";
            });

        Add(
            scenarios,
            "open-file-returns-owned-preview-request-only",
            () =>
            {
                ExplorerWorkspace workspace = new();
                ExplorerOpenResult result = workspace.OpenItem(
                    ExplorerWorkspace.ProjectBriefId);
                ExplorerPreviewRequest? request = result.PreviewRequest;
                return result.Disposition ==
                        ExplorerOpenDisposition.PreviewRequested &&
                    result.CurrentLocationId ==
                        ExplorerWorkspace.DocumentsId &&
                    request is not null &&
                    request.ItemId == ExplorerWorkspace.ProjectBriefId &&
                    request.Owner == ExplorerWorkspace.PreviewOwner &&
                    request.OwnProcessOnly &&
                    !request.OperatingSystemLaunchSupported &&
                    !request.RealFileSystemAccessSupported &&
                    !string.IsNullOrWhiteSpace(request.PreviewKind);
            });

        Add(
            scenarios,
            "open-without-selection-is-an-inert-result",
            () =>
            {
                ExplorerWorkspace workspace = new();
                ExplorerOpenResult result = workspace.OpenSelected();
                return result.Disposition ==
                        ExplorerOpenDisposition.NoSelection &&
                    result.PreviewRequest is null &&
                    workspace.CurrentLocationId ==
                        ExplorerWorkspace.DocumentsId;
            });

        Add(
            scenarios,
            "rename-validation-keeps-edit-session-correctable",
            () =>
            {
                ExplorerWorkspace workspace = new();
                _ = workspace.SelectItem(ExplorerWorkspace.ProjectBriefId);
                _ = workspace.BeginRenameSelected();
                (string? Name, ExplorerRenameError Error)[] invalidNames =
                [
                    ("   ", ExplorerRenameError.EmptyName),
                    (new string('A', ExplorerWorkspace.MaximumNameLength + 1),
                        ExplorerRenameError.NameTooLong),
                    ("BAD?.DOCX", ExplorerRenameError.InvalidCharacter),
                    ("BAD-NAME.", ExplorerRenameError.TrailingSpaceOrPeriod),
                    ("con.txt", ExplorerRenameError.ReservedName),
                ];
                foreach ((string? name, ExplorerRenameError expected) in
                    invalidNames)
                {
                    ExplorerRenameResult result =
                        workspace.CommitRename(name);
                    if (result.Succeeded ||
                        result.Error != expected ||
                        workspace.RenameItemId !=
                            ExplorerWorkspace.ProjectBriefId)
                    {
                        return false;
                    }
                }

                return workspace.CancelRename() &&
                    workspace.RenameItemId is null &&
                    workspace.Items.Single(item =>
                        item.Id == ExplorerWorkspace.ProjectBriefId).Name ==
                            "PROJECT_BRIEF_0826.DOCX";
            });

        Add(
            scenarios,
            "rename-rejects-case-insensitive-sibling-duplicate",
            () =>
            {
                ExplorerWorkspace workspace = new();
                _ = workspace.SelectItem(ExplorerWorkspace.ProjectBriefId);
                _ = workspace.BeginRenameSelected();
                ExplorerRenameResult result = workspace.CommitRename(
                    "specifications_v2.pdf");
                return !result.Succeeded &&
                    result.Error == ExplorerRenameError.DuplicateName &&
                    workspace.RenameItemId ==
                        ExplorerWorkspace.ProjectBriefId &&
                    workspace.Items.Single(item =>
                        item.Id == ExplorerWorkspace.ProjectBriefId).Name ==
                            "PROJECT_BRIEF_0826.DOCX";
            });

        Add(
            scenarios,
            "rename-cancel-preserves-original-name-and-selection",
            () =>
            {
                ExplorerWorkspace workspace = new();
                _ = workspace.SelectItem(ExplorerWorkspace.SpecificationsId);
                ExplorerRenameResult started =
                    workspace.BeginRenameSelected();
                bool cancelled = workspace.CancelRename();
                ExplorerWorkspaceItem selected = workspace.Items.Single(
                    item => item.Id == ExplorerWorkspace.SpecificationsId);
                return started.Succeeded &&
                    cancelled &&
                    selected.Name == "SPECIFICATIONS_V2.PDF" &&
                    selected.IsSelected &&
                     !selected.IsRenameTarget;
             });

        Add(
            scenarios,
            "rename-extension-synchronizes-type-icon-and-preview",
            () =>
            {
                ExplorerWorkspace known = new();
                _ = known.SelectItem(ExplorerWorkspace.ProjectBriefId);
                _ = known.BeginRenameSelected();
                ExplorerRenameResult knownRename = known.CommitRename(
                    "PROJECT_BRIEF_0826.TXT");
                ExplorerWorkspaceItem knownItem = known.Items.Single(item =>
                    item.Id == ExplorerWorkspace.ProjectBriefId);
                ExplorerPreviewRequest? knownPreview =
                    known.OpenSelected().PreviewRequest;

                ExplorerWorkspace fallback = new();
                _ = fallback.SelectItem(ExplorerWorkspace.ProjectBriefId);
                _ = fallback.BeginRenameSelected();
                ExplorerRenameResult fallbackRename = fallback.CommitRename(
                    "PROJECT_BRIEF_0826.BIN");
                ExplorerWorkspaceItem fallbackItem = fallback.Items.Single(
                    item => item.Id == ExplorerWorkspace.ProjectBriefId);
                ExplorerPreviewRequest? fallbackPreview =
                    fallback.OpenSelected().PreviewRequest;

                return knownRename.Succeeded &&
                    knownItem.TypeLabel == "TXT" &&
                    knownItem.IconKey == "text" &&
                    knownPreview is not null &&
                    knownPreview.TypeLabel == "TXT" &&
                    knownPreview.PreviewKind == "text-outline" &&
                    fallbackRename.Succeeded &&
                    fallbackItem.TypeLabel == "BIN" &&
                    fallbackItem.IconKey == "document" &&
                    fallbackPreview is not null &&
                    fallbackPreview.TypeLabel == "BIN" &&
                    fallbackPreview.PreviewKind == "document-outline";
            });

        Add(
            scenarios,
            "long-name-round-trips-without-model-truncation",
            () =>
            {
                ExplorerWorkspace workspace = new();
                _ = workspace.TryNavigateTo(ExplorerWorkspace.ProjectsId);
                ExplorerWorkspaceItem seeded = workspace.Items.Single(
                    item => item.Id == ExplorerWorkspace.LongResearchFileId);
                string longName = new string('A', 252) + ".MD";
                _ = workspace.SelectItem(
                    ExplorerWorkspace.LongResearchFileId);
                _ = workspace.BeginRenameSelected();
                ExplorerRenameResult result =
                    workspace.CommitRename(longName);
                ExplorerWorkspaceItem renamed = workspace.Items.Single(
                    item => item.Id == ExplorerWorkspace.LongResearchFileId);
                return seeded.Name.Length > 80 &&
                    longName.Length == ExplorerWorkspace.MaximumNameLength &&
                    result.Succeeded &&
                    renamed.Name == longName &&
                    renamed.Name.Length == ExplorerWorkspace.MaximumNameLength;
            });

        Add(
            scenarios,
            "new-folder-names-and-identities-are-unique-and-deterministic",
            () =>
            {
                ExplorerWorkspace workspace = new();
                _ = workspace.TryNavigateTo(ExplorerWorkspace.ArchiveId);
                ExplorerWorkspaceItem first = workspace.CreateUniqueFolder();
                _ = workspace.CancelRename();
                ExplorerWorkspaceItem second = workspace.CreateUniqueFolder();
                _ = workspace.CancelRename();
                ExplorerWorkspaceItem third = workspace.CreateUniqueFolder();
                _ = workspace.CancelRename();

                ExplorerWorkspace replay = new();
                _ = replay.TryNavigateTo(ExplorerWorkspace.ArchiveId);
                ExplorerWorkspaceItem replayFirst =
                    replay.CreateUniqueFolder();
                string[] expectedNames =
                [
                    "New Folder",
                    "New Folder (2)",
                    "New Folder (3)",
                ];
                ExplorerWorkspaceItem[] created = [first, second, third];
                return created.Select(item => item.Name)
                        .SequenceEqual(expectedNames) &&
                    created.Select(item => item.Id)
                        .Distinct(StringComparer.Ordinal).Count() == 3 &&
                    first.Id == replayFirst.Id &&
                    first.Name == replayFirst.Name &&
                    workspace.Items.Count == 3;
            });

        Add(
            scenarios,
            "new-folder-enters-selected-inline-rename-state",
            () =>
            {
                ExplorerWorkspace workspace = new();
                _ = workspace.TryNavigateTo(ExplorerWorkspace.ArchiveId);
                ExplorerWorkspaceItem folder =
                    workspace.CreateUniqueFolder();
                ExplorerWorkspaceItem live = workspace.Items.Single();
                return folder.Kind == ExplorerWorkspaceItemKind.Folder &&
                    folder.IsSelected &&
                    folder.IsRenameTarget &&
                    live.Id == workspace.SelectedItemId &&
                    live.Id == workspace.RenameItemId &&
                    !live.IsPreviewable;
            });

        Add(
            scenarios,
            "empty-location-has-explicit-stable-state",
            () =>
            {
                ExplorerWorkspace workspace = new();
                bool navigated = workspace.TryNavigateTo(
                    ExplorerWorkspace.ArchiveId);
                ExplorerWorkspaceSnapshot empty =
                    workspace.CaptureSnapshot();
                ExplorerOpenResult inert = workspace.OpenSelected();
                _ = workspace.CreateUniqueFolder();
                ExplorerWorkspaceSnapshot populated =
                    workspace.CaptureSnapshot();
                return navigated &&
                    empty.IsEmpty &&
                    empty.Items.Count == 0 &&
                    empty.SelectedItemId is null &&
                    inert.Disposition == ExplorerOpenDisposition.NoSelection &&
                    !populated.IsEmpty &&
                    populated.Items.Count == 1;
            });

        Add(
            scenarios,
            "same-location-navigation-does-not-pollute-history",
            () =>
            {
                ExplorerWorkspace workspace = new();
                bool navigated = workspace.TryNavigateTo(
                    ExplorerWorkspace.DocumentsId);
                return !navigated &&
                    !workspace.CanGoBack &&
                    !workspace.CanGoForward &&
                    workspace.CurrentLocationId ==
                        ExplorerWorkspace.DocumentsId;
            });

        Add(
            scenarios,
            "hosted-surface-presents-initial-workspace-state",
            () => WithHostedSurface(surface =>
                surface.ExplorerBreadcrumbForTest ==
                    "THIS PC  /  NEURAL STORAGE  /  DOCUMENTS" &&
                surface.ExplorerVisibleItemCountForTest == 6 &&
                !surface.ExplorerEmptyStateVisibleForTest &&
                surface.ExplorerSelectedItemIdForTest is null &&
                !surface.ExplorerOpenEnabledForTest &&
                !surface.ExplorerRenameEnabledForTest &&
                !surface.ExplorerBackEnabledForTest &&
                !surface.ExplorerForwardEnabledForTest &&
                surface.ExplorerUpEnabledForTest));

        Add(
            scenarios,
            "hosted-surface-selection-synchronizes-actions",
            () => WithHostedSurface(surface =>
            {
                surface.ExplorerItemList.SelectedIndex = 0;
                PumpDispatcher();
                return surface.ExplorerWorkspaceForTest.SelectedItemId ==
                        ExplorerWorkspace.ProjectBriefId &&
                    surface.ExplorerSelectedItemIdForTest ==
                        ExplorerWorkspace.ProjectBriefId &&
                    surface.ExplorerOpenEnabledForTest &&
                    surface.ExplorerRenameEnabledForTest;
            }));

        Add(
            scenarios,
            "hosted-surface-file-open-stays-in-owned-preview",
            () => WithHostedSurface(surface =>
            {
                surface.ExplorerItemList.SelectedIndex = 0;
                Invoke(surface.ExplorerOpenButton);
                PumpDispatcher();
                bool opened =
                    surface.ExplorerWorkspaceForTest.CurrentLocationId ==
                        ExplorerWorkspace.DocumentsId &&
                    surface.InteractionStatusText.Text ==
                        "EXPLORER / OWNED PREVIEW / " +
                        "PROJECT_BRIEF_0826.DOCX" &&
                    surface.ExplorerPreviewVisibleForTest &&
                    surface.ExplorerPreviewNameForTest ==
                        "PROJECT_BRIEF_0826.DOCX" &&
                    !surface.ExplorerItemListEnabledForTest &&
                    surface.ExplorerPreviewCyclesKeyboardFocusForTest;
                Invoke(surface.ExplorerPreviewCloseButton);
                PumpDispatcher();
                return opened &&
                    !surface.ExplorerPreviewVisibleForTest &&
                    surface.ExplorerItemListEnabledForTest;
            }));

        Add(
            scenarios,
            "hosted-surface-navigation-and-empty-state-stay-synchronized",
            () => WithHostedSurface(surface =>
            {
                bool archive = surface.ExplorerWorkspaceForTest.TryNavigateTo(
                    ExplorerWorkspace.ArchiveId);
                PumpDispatcher();
                bool empty = archive &&
                    surface.ExplorerVisibleItemCountForTest == 0 &&
                    surface.ExplorerEmptyStateVisibleForTest &&
                    surface.ExplorerBreadcrumbForTest ==
                        "THIS PC  /  NEURAL STORAGE  /  ARCHIVE" &&
                    surface.ExplorerBackEnabledForTest;
                Invoke(surface.ExplorerBackButton);
                PumpDispatcher();
                return empty &&
                    surface.ExplorerWorkspaceForTest.CurrentLocationId ==
                        ExplorerWorkspace.DocumentsId &&
                    surface.ExplorerVisibleItemCountForTest == 6 &&
                    !surface.ExplorerEmptyStateVisibleForTest &&
                    surface.ExplorerForwardEnabledForTest;
            }));

        Add(
            scenarios,
            "hosted-surface-new-folder-enters-inline-rename-state",
            () => WithHostedSurface(surface =>
            {
                _ = surface.ExplorerWorkspaceForTest.TryNavigateTo(
                    ExplorerWorkspace.ArchiveId);
                Invoke(surface.ExplorerNewFolderButton);
                PumpDispatcher();
                return surface.ExplorerVisibleItemCountForTest == 1 &&
                    surface.ExplorerWorkspaceForTest.RenameItemId is not null &&
                    surface.ExplorerSelectedItemIdForTest ==
                        surface.ExplorerWorkspaceForTest.RenameItemId &&
                    !surface.ExplorerEmptyStateVisibleForTest;
            }));

        Add(
            scenarios,
            "hosted-taskbar-new-explorer-creates-real-independent-surface",
            () => WithHostedSurface(surface =>
            {
                ContextMenu? menu = surface.TaskbarExplorerButton.ContextMenu;
                if (menu is null)
                {
                    throw new InvalidOperationException(
                        "New Explorer window is absent from TaskbarExplorerButton.ContextMenu.");
                }

                if (menu.IsOpen)
                {
                    return false;
                }

                menu.PlacementTarget = surface.TaskbarExplorerButton;
                menu.IsOpen = true;
                PumpDispatcher();
                MenuItem? create = menu.Items.OfType<MenuItem>().SingleOrDefault(
                    item => Equals(item.Header, "_New Explorer window"));
                if (create is null || !create.IsEnabled)
                {
                    throw new InvalidOperationException("Discoverable New Explorer window action is absent.");
                }

                create.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.IsOpen = false;
                PumpDispatcher();
                FrameworkElement[] explorers = Descendants(surface)
                    .OfType<FrameworkElement>()
                    .Where(element => element.GetType().Name == "ExplorerSurface")
                    .ToArray();
                return surface.VisibleOwnedWindowCount == 2 &&
                    surface.ActiveOwnedWindowId is not null and not "explorer" &&
                    explorers.Length == 2 &&
                    explorers.All(explorer => Descendants(explorer)
                        .OfType<ListBox>().Any(list => list.Items.Count == 6));
            }));

        Add(scenarios, "hosted-sessions-own-navigation-selection-rename-folder-and-preview",
            () => WithHostedSurface(shell =>
            {
                string secondaryId = CreateViaTaskbarMenu(shell);
                ExplorerSurface primary = shell.GetExplorerSurfaceForTest("explorer");
                ExplorerSurface secondary = shell.GetExplorerSurfaceForTest(secondaryId);
                Require(!ReferenceEquals(primary.Workspace, secondary.Workspace), "workspaces must be independent");
                Invoke(secondary.ExplorerUpButton);
                PumpDispatcher();
                Require(secondary.Workspace.CurrentLocationId == ExplorerWorkspace.NeuralStorageId &&
                    primary.Workspace.CurrentLocationId == ExplorerWorkspace.DocumentsId, "secondary navigation crossed sessions");
                secondary.ExplorerItemList.SelectedItem = secondary.Workspace.Items.Single(item => item.Id == ExplorerWorkspace.ProjectsId);
                Invoke(secondary.ExplorerOpenButton);
                PumpDispatcher();
                Invoke(secondary.ExplorerBackButton);
                PumpDispatcher();
                Invoke(secondary.ExplorerForwardButton);
                PumpDispatcher();
                Require(secondary.Workspace.CurrentLocationId == ExplorerWorkspace.ProjectsId &&
                    !primary.Workspace.CanGoBack, "history is not independent");
                _ = secondary.Workspace.TryNavigateTo(ExplorerWorkspace.ArchiveId);
                Invoke(secondary.ExplorerNewFolderButton);
                PumpDispatcher();
                TextBox folderEditor = RenameEditor(secondary);
                folderEditor.Text = "SESSION_FOLDER";
                RaiseKey(folderEditor, System.Windows.Input.Key.Enter);
                PumpDispatcher();
                Require(secondary.Workspace.Items.Single().Name == "SESSION_FOLDER" &&
                    primary.Workspace.Items.Count == 6, "new folder changed the other tree");
                Invoke(secondary.ExplorerOpenButton);
                PumpDispatcher();
                Require(secondary.ExplorerEmptyState.Visibility == Visibility.Visible &&
                    primary.ExplorerItemList.Items.Count == 6, "new folder empty state is not local");
                _ = secondary.Workspace.TryNavigateTo(ExplorerWorkspace.DocumentsId);
                secondary.ExplorerItemList.SelectedIndex = 0;
                primary.ExplorerItemList.SelectedIndex = 0;
                Invoke(primary.ExplorerRenameButton);
                PumpDispatcher();
                TextBox primaryEditor = RenameEditor(primary);
                _ = primaryEditor.Focus();
                primaryEditor.Text = "PRIMARY_ONLY.TXT";
                Invoke(secondary.ExplorerRenameButton);
                // Same seeded item ID, but an editor from a different surface.
                // A routed key raised on the secondary must not consume that editor.
                RaiseKey(secondary, System.Windows.Input.Key.Enter);
                Require(secondary.Workspace.Items[0].Name == "PROJECT_BRIEF_0826.DOCX" &&
                    secondary.Workspace.RenameItemId == ExplorerWorkspace.ProjectBriefId,
                    "shared seed ID cross-routed the primary rename editor into secondary");
                _ = secondary.Workspace.CancelRename();
                RaiseKey(primaryEditor, System.Windows.Input.Key.Enter);
                PumpDispatcher();
                Require(primary.Workspace.Items[0].Name == "PRIMARY_ONLY.TXT" &&
                    secondary.Workspace.Items[0].Name == "PROJECT_BRIEF_0826.DOCX", "rename leaked across workspaces");
                _ = secondary.ExplorerItemList.Focus();
                Invoke(secondary.ExplorerRenameButton);
                PumpDispatcher();
                TextBox editor = RenameEditor(secondary);
                editor.Text = "BAD?.TXT";
                RaiseKey(editor, System.Windows.Input.Key.Enter);
                PumpDispatcher();
                Require(secondary.Workspace.RenameItemId is not null &&
                    secondary.ExplorerRenameErrorText.Visibility == Visibility.Visible && editor.IsKeyboardFocused,
                    "secondary invalid rename must remain correctable");
                editor.Text = "SECONDARY_ONLY.MD";
                RaiseKey(editor, System.Windows.Input.Key.Enter);
                PumpDispatcher();
                Invoke(secondary.ExplorerOpenButton);
                PumpDispatcher();
                Require(secondary.ExplorerPreviewPanel.Visibility == Visibility.Visible &&
                    secondary.ExplorerPreviewNameText.Text == "SECONDARY_ONLY.MD" &&
                    primary.ExplorerPreviewPanel.Visibility == Visibility.Collapsed,
                    "preview is not owned by secondary");
                Invoke(secondary.ExplorerPreviewCloseButton);
                PumpDispatcher();
                secondary.ExplorerItemList.SelectedIndex = 1;
                Require(primary.Workspace.SelectedItemId == ExplorerWorkspace.ProjectBriefId &&
                    secondary.Workspace.SelectedItemId == ExplorerWorkspace.SpecificationsId,
                    "selection shared an identity across sessions");
                return true;
            }));

        Add(scenarios, "hosted-keyboard-menu-four-session-cap-is-inert-and-reopenable",
            () => WithHostedSurface(shell =>
            {
                for (int index = 0; index < 3; index++) _ = CreateViaTaskbarMenu(shell);
                string[] ids = shell.ExplorerSessionIdsForTest.ToArray();
                Require(ids.Length == 4 && ids.Distinct().Count() == 4, "four distinct real sessions required");
                ExplorerSurface retained = shell.GetExplorerSurfaceForTest(ids[1]);
                RaiseKey(shell.TaskbarExplorerButton, System.Windows.Input.Key.Apps);
                PumpDispatcher();
                ContextMenu menu = shell.TaskbarExplorerButton.ContextMenu;
                MenuItem create = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_New Explorer window"));
                Require(!create.IsEnabled &&
                    System.Windows.Automation.AutomationProperties.GetHelpText(create).Contains("4", StringComparison.Ordinal) &&
                    System.Windows.Automation.AutomationProperties.GetHelpText(create).Contains("retained", StringComparison.OrdinalIgnoreCase),
                    "New must be disabled with a clear retained-session cap explanation");
                Require(menu.IsOpen, "Apps key must open discoverable Explorer session commands");
                string? active = shell.ActiveOwnedWindowId;
                Rect[] bounds = ids.Select(shell.GetOwnedWindowBoundsForTest).ToArray();
                for (int index = 0; index < 3; index++) create.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                PumpDispatcher();
                Require(shell.ExplorerSessionIdsForTest.SequenceEqual(ids) && shell.ActiveOwnedWindowId == active &&
                    ids.Select(shell.GetOwnedWindowBoundsForTest).SequenceEqual(bounds), "cap action is not inert");
                menu.IsOpen = false;
                Invoke(OwnedWindowLayoutScenarios.ExplorerTitleButton(shell, ids[1], "Close"));
                PumpDispatcher();
                OwnedWindowLayoutScenarios.InvokeSessionMenu(shell, ids[1], "Reopen");
                PumpDispatcher();
                Require(ReferenceEquals(retained, shell.GetExplorerSurfaceForTest(ids[1])) &&
                    shell.ExplorerSessionIdsForTest.Count == 4 && !create.IsEnabled,
                    "closing must retain identity and may not free a fifth allocation slot");
                foreach (string id in ids)
                    Invoke(OwnedWindowLayoutScenarios.ExplorerTitleButton(shell, id, "Minimize"));
                PumpDispatcher();
                Require(shell.ExplorerRunningIndicator.Visibility == Visibility.Visible && shell.ExplorerRunningIndicator.Opacity == 0.45,
                    "aggregate running marker must distinguish all minimized from all closed");
                foreach (string id in ids)
                    Invoke(OwnedWindowLayoutScenarios.ExplorerTitleButton(shell, id, "Close"));
                PumpDispatcher();
                Require(shell.ExplorerRunningIndicator.Visibility == Visibility.Collapsed, "closed retained sessions are not running");
                OwnedWindowLayoutScenarios.InvokeSessionMenu(shell, ids[2], "Reopen");
                PumpDispatcher();
                return shell.ActiveOwnedWindowId == ids[2] && shell.VisibleOwnedWindowCount == 1 &&
                    shell.ExplorerSessionIdsForTest.Count == 4 && shell.ExplorerRunningIndicator.Opacity == 1.0;
            }));

        Add(scenarios, "hosted-deferred-focus-respects-session-lifecycle-intent-and-unload",
            () => WithHostedSurface(shell =>
            {
                string id = CreateViaTaskbarMenu(shell);
                ExplorerSurface secondary = shell.GetExplorerSurfaceForTest(id);
                ExplorerSurface primary = shell.GetExplorerSurfaceForTest("explorer");
                secondary.ExplorerItemList.SelectedIndex = 0;
                _ = secondary.ExplorerItemList.Focus();
                Invoke(secondary.ExplorerRenameButton);
                _ = primary.ExplorerItemList.Focus();
                PumpDispatcher();
                Require(primary.ExplorerItemList.IsKeyboardFocusWithin && shell.ActiveOwnedWindowId == "explorer",
                    "stale secondary rename focus stole newer primary focus intent");
                _ = secondary.Workspace.CancelRename();
                _ = secondary.ExplorerItemList.Focus();
                Invoke(secondary.ExplorerRenameButton);
                PumpDispatcher();
                TextBox editor = RenameEditor(secondary);
                editor.Text = "BAD?.TXT";
                RaiseKey(editor, System.Windows.Input.Key.Enter);
                _ = primary.ExplorerItemList.Focus();
                PumpDispatcher();
                Require(primary.ExplorerItemList.IsKeyboardFocusWithin && shell.ActiveOwnedWindowId == "explorer",
                    "invalid-rename correction focus stole newer primary intent");
                _ = secondary.Workspace.CancelRename();
                _ = secondary.ExplorerItemList.Focus();
                Invoke(secondary.ExplorerNewFolderButton);
                Invoke(OwnedWindowLayoutScenarios.ExplorerTitleButton(shell, id, "Minimize"));
                PumpDispatcher();
                Require(!secondary.IsKeyboardFocusWithin && shell.ActiveOwnedWindowId != id,
                    "hidden session executed deferred new-folder focus");
                OwnedWindowLayoutScenarios.InvokeSessionMenu(shell, id, "Restore");
                PumpDispatcher();
                Require(secondary.IsKeyboardFocusWithin && shell.ActiveOwnedWindowId == id,
                    "exact restore did not honor current session focus intent");
                _ = secondary.Workspace.CancelRename();
                secondary.ExplorerItemList.SelectedIndex = 0;
                Invoke(secondary.ExplorerOpenButton);
                Invoke(OwnedWindowLayoutScenarios.ExplorerTitleButton(shell, id, "Close"));
                PumpDispatcher();
                Require(!secondary.IsKeyboardFocusWithin, "closed session executed deferred preview focus");
                OwnedWindowLayoutScenarios.InvokeSessionMenu(shell, id, "Reopen");
                PumpDispatcher();
                Require(secondary.ExplorerPreviewPanel.Visibility == Visibility.Visible &&
                    secondary.ExplorerPreviewCloseButton.IsKeyboardFocused,
                    "owned preview and its focus target were not retained on exact reopen");
                Invoke(secondary.ExplorerPreviewCloseButton);
                Invoke(secondary.ExplorerRenameButton);
                shell.FocusLayoutRailForTest();
                PumpDispatcher();
                Require(shell.LayoutRailHasKeyboardFocusForTest, "pending Explorer work stole rail focus");
                shell.RaiseRailKeyForTest(System.Windows.Input.Key.Escape);
                PumpDispatcher();
                Require(secondary.IsKeyboardFocusWithin, "explicit rail return did not regain owning session");
                Window host = Window.GetWindow(shell);
                Invoke(secondary.ExplorerNewFolderButton);
                _ = secondary.ExplorerItemList.CaptureMouse();
                host.Content = null;
                PumpDispatcher();
                Require(System.Windows.Input.Mouse.Captured is null &&
                    shell.ExplorerSessionIdsForTest.All(sessionId =>
                        !shell.GetExplorerSurfaceForTest(sessionId).IsLoaded &&
                        !HasPendingDispatcherWork(shell.GetExplorerSurfaceForTest(sessionId))),
                    "unload leaked instance capture or deferred focus work");
                return true;
            }));

        Add(scenarios, "hosted-keyboard-new-and-restore-preserve-newer-external-focus",
            () => WithHostedSurface(shell =>
            {
                RaiseKey(shell.TaskbarExplorerButton, System.Windows.Input.Key.Apps);
                PumpDispatcher();
                ContextMenu menu = shell.TaskbarExplorerButton.ContextMenu;
                MenuItem create = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_New Explorer window"));
                _ = create.Focus();
                RaiseMenuEnter(create);
                PumpDispatcher();
                Require(shell.ExplorerSessionIdsForTest.Count == 2 && !menu.IsOpen && shell.ActiveOwnedWindowId is not null and not "explorer",
                    "keyboard Enter on New Explorer window did not create a real session and close menu");
                string id = shell.ActiveOwnedWindowId!;
                ExplorerSurface surface = shell.GetExplorerSurfaceForTest(id);
                surface.ExplorerItemList.SelectedIndex = 0;
                _ = surface.ExplorerItemList.Focus();
                Invoke(surface.ExplorerRenameButton);
                _ = shell.TaskbarSearchButton.Focus();
                PumpDispatcher();
                Require(shell.TaskbarSearchButton.IsKeyboardFocused,
                    "pending rename focus overrode newer external taskbar keyboard intent");
                Invoke(OwnedWindowLayoutScenarios.ExplorerTitleButton(shell, id, "Minimize"));
                PumpDispatcher();
                RaiseKey(shell.TaskbarExplorerButton, System.Windows.Input.Key.Apps);
                PumpDispatcher();
                MenuItem restore = menu.Items.OfType<MenuItem>().Single(item => item.CommandParameter is OwnedWindowId owned && owned.Value == id);
                _ = restore.Focus();
                RaiseMenuEnter(restore);
                PumpDispatcher();
                Require(surface.IsKeyboardFocusWithin && shell.ActiveOwnedWindowId == id && !menu.IsOpen,
                    "keyboard ID-bearing Restore did not focus its exact retained session");
                return true;
            }));

        Add(scenarios, "hosted-already-focused-invalid-rename-blur-preserves-search-focus",
            () => HostedRenameBlurPreservesNewerFocus(validName: false, foreignExplorer: false));
        Add(scenarios, "hosted-already-focused-valid-rename-blur-preserves-search-focus",
            () => HostedRenameBlurPreservesNewerFocus(validName: true, foreignExplorer: false));
        Add(scenarios, "hosted-already-focused-invalid-rename-blur-preserves-foreign-explorer",
            () => HostedRenameBlurPreservesNewerFocus(validName: false, foreignExplorer: true));
        Add(scenarios, "hosted-already-focused-valid-rename-blur-preserves-foreign-explorer",
            () => HostedRenameBlurPreservesNewerFocus(validName: true, foreignExplorer: true));

        int passedCount = scenarios.Count(scenario => scenario.Passed);
        return new(
            1,
            "jarvisv2-explorer-workspace-scenarios",
            passedCount == scenarios.Count ? "passed" : "failed",
            scenarios.Count,
            passedCount,
            true,
            false,
            false,
            scenarios);
    }

    public static int RunCommand() => RunCommand(Console.Out);

    public static int RunCommand(System.IO.TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ExplorerWorkspaceTestReceipt receipt = Run();
        output.WriteLine(JsonSerializer.Serialize(receipt, JsonOptions));
        return receipt.Result == "passed" ? 0 : 12;
    }

    private static bool HostedRenameBlurPreservesNewerFocus(bool validName, bool foreignExplorer) =>
        WithHostedSurface(shell =>
        {
            string id = CreateViaTaskbarMenu(shell);
            ExplorerSurface secondary = shell.GetExplorerSurfaceForTest(id);
            ExplorerSurface primary = shell.GetExplorerSurfaceForTest("explorer");
            secondary.ExplorerItemList.SelectedIndex = 0;
            _ = secondary.ExplorerItemList.Focus();
            Invoke(secondary.ExplorerRenameButton);
            PumpDispatcher();
            TextBox editor = RenameEditor(secondary);
            Require(ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, editor) &&
                editor.IsKeyboardFocused && shell.ActiveOwnedWindowId == id,
                "blur regression requires an already-focused owning editor, not pending initial focus");
            string name = validName ? "BLUR_ONLY.TXT" : "BAD?.TXT";
            editor.Text = name;
            int reactivations = 0;
            secondary.ActivationRequested += () => reactivations++;
            Control destination = foreignExplorer ? primary.ExplorerItemList : shell.TaskbarSearchButton;
            _ = destination.Focus();
            Require(ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, destination),
                "newer blur destination must actually receive keyboard focus before draining");
            PumpDispatcher();
            ExplorerWorkspaceItem item = secondary.Workspace.Items.Single(item => item.Id == ExplorerWorkspace.ProjectBriefId);
            bool statePreserved = validName
                ? secondary.Workspace.RenameItemId is null && item.Name == name && item.TypeLabel == "TXT" &&
                    secondary.ExplorerRenameErrorText.Visibility == Visibility.Collapsed
                : secondary.Workspace.RenameItemId == item.Id && item.Name == "PROJECT_BRIEF_0826.DOCX" &&
                    secondary.ExplorerRenameErrorText.Visibility == Visibility.Visible && editor.IsVisible && editor.Text == name;
            Require(ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, destination) &&
                shell.ActiveOwnedWindowId == (foreignExplorer ? "explorer" : id) && reactivations == 0 &&
                !HasPendingDispatcherWork(secondary) && statePreserved &&
                primary.Workspace.Items[0].Name == "PROJECT_BRIEF_0826.DOCX" && primary.Workspace.RenameItemId is null,
                $"already-focused blur/valid={validName}/foreign={foreignExplorer}: " +
                $"focus={(System.Windows.Input.Keyboard.FocusedElement as FrameworkElement)?.Name}, " +
                $"active={shell.ActiveOwnedWindowId}, reactivations={reactivations}, statePreserved={statePreserved}");
            return true;
        });

    private static string Signature(ExplorerWorkspaceSnapshot snapshot) =>
        string.Join(
            "|",
            snapshot.Breadcrumb.Select(segment =>
                $"B:{segment.Id}:{segment.Name}")
                .Concat(snapshot.Items.Select(item =>
                    $"I:{item.Id}:{item.Name}:{item.Kind}:{item.TypeLabel}:" +
                    $"{item.SizeBytes}:{item.ModifiedAt:O}")));

    private static bool WithHostedSurface(
        Func<DesktopShellSurface, bool> action)
    {
        DesktopShellSurface surface = new();
        Window host = new()
        {
            Width = 1600,
            Height = 900,
            Left = -32000,
            Top = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = surface,
        };
        try
        {
            host.Show();
            surface.Measure(new Size(1600, 900));
            surface.Arrange(new Rect(0, 0, 1600, 900));
            surface.UpdateLayout();
            PumpDispatcher();
            return action(surface);
        }
        finally
        {
            host.Content = null;
            host.Close();
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        DispatcherFrame frame = new();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    internal static string CreateViaTaskbarMenu(DesktopShellSurface shell)
    {
        ContextMenu menu = shell.TaskbarExplorerButton.ContextMenu ??
            throw new InvalidOperationException("Explorer session menu absent");
        menu.PlacementTarget = shell.TaskbarExplorerButton;
        menu.IsOpen = true;
        PumpDispatcher();
        MenuItem create = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_New Explorer window"));
        Require(create.IsEnabled, "New Explorer window disabled before cap");
        create.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.IsOpen = false;
        PumpDispatcher();
        return shell.ActiveOwnedWindowId ?? throw new InvalidOperationException("created Explorer not active");
    }

    private static TextBox RenameEditor(ExplorerSurface surface) =>
        Descendants(surface).OfType<TextBox>().First(editor => editor.IsVisible && editor.DataContext is ExplorerWorkspaceItem { IsRenameTarget: true });

    internal static void RaiseKey(UIElement source, System.Windows.Input.Key key) =>
        source.RaiseEvent(new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(source) ?? throw new InvalidOperationException("key source not hosted"),
            Environment.TickCount, key) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });

    private static void RaiseMenuEnter(MenuItem source) =>
        source.RaiseEvent(new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(source) ?? throw new InvalidOperationException("menu source not hosted"),
            Environment.TickCount, System.Windows.Input.Key.Enter) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });

    private static bool HasPendingDispatcherWork(ExplorerSurface surface) =>
        surface.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Any(field => field.GetValue(surface) is DispatcherOperation { Status: DispatcherOperationStatus.Pending });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (DependencyObject nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static void Invoke(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Add(
        ICollection<ExplorerWorkspaceScenario> scenarios,
        string name,
        Func<bool> action)
    {
        try
        {
            bool passed = action();
            scenarios.Add(
                new(
                    name,
                    passed,
                    passed ? "passed" : "assertion-failed"));
        }
        catch (Exception exception)
        {
            scenarios.Add(
                new(
                    name,
                    false,
                    $"{exception.GetType().Name}: {exception.Message}"));
        }
    }
}
