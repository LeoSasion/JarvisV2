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

        Add(scenarios, "invalid-selection-notifications-null-foreign-and-already-clear",
            () =>
            {
                foreach (string? invalid in new string?[]
                {
                    null, "file:not-present", ExplorerWorkspace.ProjectsId,
                })
                {
                    ExplorerWorkspace workspace = new();
                    int notifications = 0;
                    bool returned = false;
                    workspace.Changed += (sender, args) =>
                    {
                        notifications++;
                        Require(ReferenceEquals(sender, workspace) && args == EventArgs.Empty && !returned,
                            "Changed must run synchronously before invalid selection returns.");
                        ExplorerWorkspaceSnapshot snapshot = workspace.CaptureSnapshot();
                        Require(snapshot.SelectedItemId is null && snapshot.RenameItemId is null &&
                            snapshot.Items.All(item => !item.IsSelected && !item.IsRenameTarget),
                            "Invalid selection must clear selection and rename before Changed.");
                    };
                    Require(!workspace.SelectItem(invalid) && notifications == 0,
                        "An already-clear invalid selection must not notify.");
                    ExplorerWorkspace active = new();
                    Require(active.SelectItem(ExplorerWorkspace.ProjectBriefId) && active.BeginRenameSelected().Succeeded,
                        "Characterization requires active selection and rename.");
                    active.Changed += (_, _) =>
                    {
                        notifications++;
                        Require(!returned && active.SelectedItemId is null && active.RenameItemId is null,
                            "Both interaction fields must be clear inside Changed.");
                    };
                    Require(!active.SelectItem(invalid) && notifications == 1,
                        "Null, missing and foreign membership must each clear exactly once.");
                    returned = true;
                    Require(!active.SelectItem(invalid) && notifications == 1,
                        "Repeating an already-clear invalid selection must be inert.");
                }
                return true;
            });
        Add(scenarios, "invalid-selection-reentrant-subscriber-selection-survives-outer-return",
            () =>
            {
                ExplorerWorkspace workspace = new();
                Require(workspace.SelectItem(ExplorerWorkspace.ProjectBriefId) && workspace.BeginRenameSelected().Succeeded,
                    "Characterization requires active selection and rename.");
                List<string> trace = [];
                bool returned = false;
                workspace.Changed += (_, _) =>
                {
                    Require(!returned, "Every nested notification must precede the outer return.");
                    trace.Add($"event:{workspace.SelectedItemId ?? "none"}:{workspace.RenameItemId ?? "none"}");
                    if (workspace.SelectedItemId is null)
                    {
                        Require(workspace.SelectItem(ExplorerWorkspace.SpecificationsId), "Reentrant seeded selection failed.");
                        trace.Add($"subscriber:{workspace.SelectedItemId}:{workspace.RenameItemId ?? "none"}");
                    }
                };
                bool accepted = workspace.SelectItem(null);
                returned = true;
                trace.Add($"return:{accepted}:{workspace.SelectedItemId}:{workspace.RenameItemId ?? "none"}");
                Require(trace.SequenceEqual(new[]
                {
                    "event:none:none",
                    "event:file:specifications-v2:none",
                    "subscriber:file:specifications-v2:none",
                    "return:False:file:specifications-v2:none",
                }), string.Join("|", trace));
                return true;
            });

        Add(scenarios, "history-public-boundaries-and-notification-snapshots",
            () =>
            {
                ExplorerWorkspace workspace = new();
                int notifications = 0;
                Require(!workspace.TryGoBack() && !workspace.TryGoForward(), "Initial history boundaries must be inert.");
                Require(workspace.TryNavigateTo(ExplorerWorkspace.ProjectsId) &&
                    workspace.SelectItem("folder:aurora-engine") && workspace.BeginRenameSelected().Succeeded,
                    "History characterization requires selected rename in Projects.");
                List<ExplorerWorkspaceSnapshot> snapshots = [];
                bool returned = false;
                workspace.Changed += (_, _) =>
                {
                    notifications++;
                    Require(!returned, "History Changed must precede navigation return.");
                    ExplorerWorkspaceSnapshot snapshot = workspace.CaptureSnapshot();
                    Require(snapshot.SelectedItemId is null && snapshot.RenameItemId is null &&
                        snapshot.Items.All(item => !item.IsSelected && !item.IsRenameTarget),
                        "History must clear interaction before notifying.");
                    snapshots.Add(snapshot);
                };
                Require(workspace.TryGoBack(), "Back failed.");
                Require(snapshots[0].CurrentLocation.Id == ExplorerWorkspace.DocumentsId &&
                    !snapshots[0].CanGoBack && snapshots[0].CanGoForward, "Both history mutations must precede Changed.");
                Require(!workspace.TryGoBack() && notifications == 1, "Empty back history must not notify.");
                Require(workspace.TryGoForward(), "Forward failed.");
                Require(snapshots[1].CurrentLocation.Id == ExplorerWorkspace.ProjectsId &&
                    snapshots[1].CanGoBack && !snapshots[1].CanGoForward, "Forward history mutations must precede Changed.");
                Require(!workspace.TryGoForward() && notifications == 2, "Empty forward history must not notify.");
                returned = true;
                Require(snapshots[0].CurrentLocation.Id == ExplorerWorkspace.DocumentsId &&
                    snapshots[0].Items.Count == 6 && !snapshots[0].CanGoBack && snapshots[0].CanGoForward,
                    "Captured history snapshots must remain immutable.");
                return true;
            });
        Add(scenarios, "history-reentrant-opposite-navigation-preserves-synchronous-trace",
            () =>
            {
                foreach (bool backFirst in new[] { true, false })
                {
                    ExplorerWorkspace workspace = new();
                    Require(workspace.TryNavigateTo(ExplorerWorkspace.ProjectsId), "Projects navigation failed.");
                    if (!backFirst) Require(workspace.TryGoBack(), "Forward setup failed.");
                    string original = backFirst ? ExplorerWorkspace.ProjectsId : ExplorerWorkspace.DocumentsId;
                    string destination = backFirst ? ExplorerWorkspace.DocumentsId : ExplorerWorkspace.ProjectsId;
                    Require(workspace.SelectItem(backFirst ? "folder:aurora-engine" : ExplorerWorkspace.ProjectBriefId) &&
                        workspace.BeginRenameSelected().Succeeded, "Reentrant history setup failed.");
                    List<string> trace = [];
                    bool nested = false;
                    bool returned = false;
                    workspace.Changed += (_, _) =>
                    {
                        Require(!returned && workspace.SelectedItemId is null && workspace.RenameItemId is null,
                            "Interaction clearing and callbacks must precede outer return.");
                        trace.Add($"event:{workspace.CurrentLocationId}:{workspace.CanGoBack}:{workspace.CanGoForward}");
                        if (!nested)
                        {
                            nested = true;
                            Require(backFirst ? workspace.TryGoForward() : workspace.TryGoBack(), "Nested opposite navigation failed.");
                            trace.Add($"subscriber:{workspace.CurrentLocationId}");
                        }
                    };
                    Require(backFirst ? workspace.TryGoBack() : workspace.TryGoForward(), "Outer navigation failed.");
                    returned = true;
                    trace.Add($"return:{workspace.CurrentLocationId}");
                    Require(trace.SequenceEqual(new[]
                    {
                        $"event:{destination}:{!backFirst}:{backFirst}",
                        $"event:{original}:{backFirst}:{!backFirst}",
                        $"subscriber:{original}",
                        $"return:{original}",
                    }), string.Join("|", trace));
                }
                return true;
            });

        Add(scenarios, "private-history-helper-moves-lists-before-notifying",
            () =>
            {
                System.Reflection.MethodInfo helper = typeof(ExplorerWorkspace).GetMethod("TryMoveHistory",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, [typeof(List<string>), typeof(List<string>)], null) ??
                    throw new InvalidOperationException("Private TryMoveHistory helper absent.");
                ExplorerWorkspace workspace = new();
                Require(workspace.SelectItem(ExplorerWorkspace.ProjectBriefId) && workspace.BeginRenameSelected().Succeeded,
                    "Private history setup failed.");
                List<string> source = [ExplorerWorkspace.ArchiveId, ExplorerWorkspace.ProjectsId];
                List<string> destination = [ExplorerWorkspace.ThisPcId];
                int notifications = 0;
                workspace.Changed += (_, _) =>
                {
                    notifications++;
                    Require(workspace.SelectedItemId is null && workspace.RenameItemId is null,
                        "Helper must clear interaction before Changed.");
                    if (notifications == 1)
                        Require(source.SequenceEqual(new[] { ExplorerWorkspace.ArchiveId }) &&
                            destination.SequenceEqual(new[] { ExplorerWorkspace.ThisPcId, ExplorerWorkspace.DocumentsId }) &&
                            workspace.CurrentLocationId == ExplorerWorkspace.ProjectsId,
                            "Pop, opposite push and location update must precede Changed.");
                    else
                        Require(source.SequenceEqual(new[] { ExplorerWorkspace.ArchiveId, ExplorerWorkspace.ProjectsId }) &&
                            destination.SequenceEqual(new[] { ExplorerWorkspace.ThisPcId }) &&
                            workspace.CurrentLocationId == ExplorerWorkspace.DocumentsId,
                            "Swapped source and destination must use the same transition.");
                };
                Require(helper.Invoke(workspace, [source, destination]) is true && notifications == 1,
                    "History helper must return true after notifying.");
                Require(helper.Invoke(workspace, [destination, source]) is true && notifications == 2,
                    "Reverse helper transition failed.");
                ExplorerWorkspaceSnapshot before = workspace.CaptureSnapshot();
                Require(helper.Invoke(workspace, [new List<string>(), destination]) is false && notifications == 2 &&
                    workspace.CaptureSnapshot().CurrentLocation == before.CurrentLocation &&
                    destination.SequenceEqual(new[] { ExplorerWorkspace.ThisPcId }), "Empty helper source must be inert.");
                return true;
            });

        Add(scenarios, "complete-seed-literal-fields-order-locations-and-owned-previews", CompleteSeedContract);

        Add(scenarios, "new-folder-reentrant-changed-advances-sequence-before-item-return",
            () =>
            {
                ExplorerWorkspace workspace = new();
                Require(workspace.TryNavigateTo(ExplorerWorkspace.ArchiveId), "Folder setup failed.");
                List<ExplorerWorkspaceSnapshot> events = [];
                ExplorerWorkspaceItem? nestedFolder = null;
                bool entered = false;
                bool returned = false;
                EventHandler onChanged = (_, _) =>
                {
                    Require(!returned, "Folder Changed must be synchronous.");
                    events.Add(workspace.CaptureSnapshot());
                    if (!entered)
                    {
                        entered = true;
                        Require(workspace.CommitRename("CALLBACK_RENAMED").Succeeded, "Callback rename failed.");
                        nestedFolder = workspace.CreateUniqueFolder();
                    }
                };
                workspace.Changed += onChanged;
                ExplorerWorkspaceItem folder = workspace.CreateUniqueFolder();
                returned = true;
                workspace.Changed -= onChanged;
                ExplorerWorkspaceItem expectedFirst = new("session:new-folder:0001", "New Folder",
                    ExplorerWorkspaceItemKind.Folder, "FOLDER", "folder", null,
                    new(2026, 8, 4, 14, 39, 1, TimeSpan.Zero), true, true, false);
                ExplorerWorkspaceItem expectedSecond = new("session:new-folder:0002", "New Folder",
                    ExplorerWorkspaceItemKind.Folder, "FOLDER", "folder", null,
                    new(2026, 8, 4, 14, 39, 2, TimeSpan.Zero), true, true, false);
                Require(events.Count == 3 && events[0].Items.SequenceEqual(new[] { expectedFirst }) &&
                    events[0].SelectedItemId == expectedFirst.Id && events[0].RenameItemId == expectedFirst.Id,
                    "First folder must be published and selected for rename before Changed.");
                Require(events[1].Items.Single() == expectedFirst with { Name = "CALLBACK_RENAMED", IsRenameTarget = false } &&
                    events[1].SelectedItemId == expectedFirst.Id && events[1].RenameItemId is null,
                    "Reentrant rename must publish before its callback.");
                Require(nestedFolder == expectedSecond && events[2].Items.SequenceEqual(new[]
                {
                    expectedFirst with { Name = "CALLBACK_RENAMED", IsSelected = false, IsRenameTarget = false }, expectedSecond,
                }) && events[2].SelectedItemId == expectedSecond.Id && events[2].RenameItemId == expectedSecond.Id,
                    "Sequence must advance before a reentrant second creation.");
                Require(folder == expectedFirst with { Name = "CALLBACK_RENAMED", IsSelected = false, IsRenameTarget = false } &&
                    events[0].Items.Single() == expectedFirst,
                    "ToItem must follow Changed and reflect reentrant mutation without changing old snapshots.");
                Require(workspace.OpenSelected().Disposition == ExplorerOpenDisposition.Navigated &&
                    workspace.CurrentLocationId == expectedSecond.Id && workspace.IsCurrentLocationEmpty &&
                    workspace.Breadcrumb.Select(segment => segment.Id).SequenceEqual(new[]
                    {
                        "location:this-pc", "location:neural-storage", "location:archive", "session:new-folder:0002",
                    }), "Created folder must be a real memory-only navigable child.");
                return true;
            });

        Add(scenarios, "private-folder-helper-publishes-linked-node-with-exact-fields",
            () =>
            {
                System.Reflection.MethodInfo helper = typeof(ExplorerWorkspace).GetMethod("AddFolder",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, [typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(DateTimeOffset)], null) ??
                    throw new InvalidOperationException("Private AddFolder helper absent.");
                ExplorerWorkspace workspace = new();
                int notifications = 0;
                workspace.Changed += (_, _) => notifications++;
                DateTimeOffset modified = new(2026, 8, 5, 1, 2, 3, TimeSpan.Zero);
                object root = helper.Invoke(workspace, ["test:root", "HELPER ROOT", null, "ROOT", "this-pc", modified]) ??
                    throw new InvalidOperationException("Folder helper did not return its Node.");
                object child = helper.Invoke(workspace, ["test:child", "HELPER STORAGE", "test:root", "STORAGE", "neural-storage", modified]) ??
                    throw new InvalidOperationException("Folder helper did not return its child Node.");
                Require(notifications == 0 && workspace.SelectedItemId is null && workspace.RenameItemId is null &&
                    workspace.CurrentLocationId == ExplorerWorkspace.DocumentsId && !workspace.CanGoBack && !workspace.CanGoForward,
                    "Construction helper must not add interaction or navigation notifications.");
                ExplorerWorkspaceItem? childItem = null;
                Require(workspace.TryGetItem("test:root", out ExplorerWorkspaceItem? rootItem) && rootItem ==
                    new ExplorerWorkspaceItem("test:root", "HELPER ROOT", ExplorerWorkspaceItemKind.Folder, "ROOT", "this-pc", null,
                        modified, false, false, false) &&
                    workspace.TryGetItem("test:child", out childItem) && childItem ==
                    new ExplorerWorkspaceItem("test:child", "HELPER STORAGE", ExplorerWorkspaceItemKind.Folder, "STORAGE", "neural-storage", null,
                        modified, false, false, false), "Helper must preserve every folder presentation field.");
                Require(root.GetType().GetProperty("ParentId")!.GetValue(root) is null &&
                    Equals(child.GetType().GetProperty("ParentId")!.GetValue(child), "test:root") &&
                    Equals(root.GetType().GetProperty("PreviewKind")!.GetValue(root), string.Empty) &&
                    Equals(child.GetType().GetProperty("PreviewKind")!.GetValue(child), string.Empty),
                    "Helper must retain null size, explicit parent and empty preview invariants.");
                Require(workspace.TryNavigateTo("test:root") && workspace.Items.SequenceEqual(new[] { childItem! }) &&
                    workspace.OpenItem("test:child").Disposition == ExplorerOpenDisposition.Navigated &&
                    workspace.CurrentLocationId == "test:child" && workspace.IsCurrentLocationEmpty &&
                    workspace.Breadcrumb.SequenceEqual(new[]
                    {
                        new ExplorerBreadcrumbSegment("test:root", "HELPER ROOT"), new("test:child", "HELPER STORAGE"),
                    }), "Returned nodes must be real linked folders on the public navigation path.");
                return true;
            });

        Add(scenarios, "rename-dot-stem-error-precedence-is-nonmutating-and-silent",
            () =>
            {
                (string? Name, ExplorerRenameError Error)[] cases =
                [
                    (null, ExplorerRenameError.EmptyName), ("", ExplorerRenameError.EmptyName),
                    ("  ", ExplorerRenameError.EmptyName),
                    (new string('A', 256) + "?.", ExplorerRenameError.NameTooLong),
                    (".", ExplorerRenameError.ReservedName), ("..", ExplorerRenameError.ReservedName),
                    ("...", ExplorerRenameError.TrailingSpaceOrPeriod),
                    ("report.", ExplorerRenameError.TrailingSpaceOrPeriod),
                    ("report ", ExplorerRenameError.TrailingSpaceOrPeriod),
                    ("CON?. ", ExplorerRenameError.TrailingSpaceOrPeriod),
                    ("BAD?.", ExplorerRenameError.TrailingSpaceOrPeriod),
                    ("CON?.txt", ExplorerRenameError.InvalidCharacter),
                    ("CON\u0001.multi.dot", ExplorerRenameError.InvalidCharacter),
                    ("CON.multi.dot", ExplorerRenameError.ReservedName),
                    ("cOn.multi.dot", ExplorerRenameError.ReservedName),
                    ("CON..txt", ExplorerRenameError.ReservedName),
                    ("specifications_v2.pdf", ExplorerRenameError.DuplicateName),
                ];
                foreach (var test in cases)
                {
                    ExplorerWorkspace workspace = new();
                    int notifications = 0;
                    workspace.Changed += (_, _) => notifications++;
                    Require(workspace.CommitRename(test.Name).Error == ExplorerRenameError.NoActiveRename && notifications == 0,
                        "No-active-rename must precede every name validation.");
                    Require(workspace.SelectItem(ExplorerWorkspace.ProjectBriefId) && workspace.BeginRenameSelected().Succeeded,
                        "Validation setup failed.");
                    int beforeCount = notifications;
                    string before = JsonSerializer.Serialize(workspace.CaptureSnapshot());
                    ExplorerRenameResult result = workspace.CommitRename(test.Name);
                    Require(!result.Succeeded && result.Error == test.Error && result.ItemId is null && result.Name is null &&
                        notifications == beforeCount && JsonSerializer.Serialize(workspace.CaptureSnapshot()) == before,
                        $"Rename precedence/mutation/notification changed for '{test.Name}': {result.Error}.");
                }
                return true;
            });
        Add(scenarios, "rename-dot-prefix-success-preserves-name-and-publishes-before-changed",
            () =>
            {
                (string Name, string Type, string Icon, string Preview)[] cases =
                [
                    ("report.v1.final.txt", "TXT", "text", "text-outline"),
                    (".CON", "FILE", "document", "document-outline"),
                    ("..CON", "CON", "document", "document-outline"),
                    (".con.txt", "TXT", "text", "text-outline"),
                    ("a..b", "B", "document", "document-outline"),
                    ("report", "FILE", "document", "document-outline"),
                    (" CON.txt", "TXT", "text", "text-outline"),
                    ("CON .txt", "TXT", "text", "text-outline"),
                    ("PROJECT_BRIEF_0826.DOCX", "DOCX", "document", "document-outline"),
                ];
                foreach (var test in cases)
                {
                    ExplorerWorkspace workspace = new();
                    Require(workspace.SelectItem(ExplorerWorkspace.ProjectBriefId) && workspace.BeginRenameSelected().Succeeded,
                        "Rename success setup failed.");
                    ExplorerWorkspaceItem before = workspace.Items[0];
                    ExplorerWorkspaceItem expected = before with
                    {
                        Name = test.Name, TypeLabel = test.Type, IconKey = test.Icon, IsRenameTarget = false,
                    };
                    int notifications = 0;
                    bool returned = false;
                    workspace.Changed += (_, _) =>
                    {
                        notifications++;
                        Require(!returned && workspace.RenameItemId is null && workspace.SelectedItemId == before.Id &&
                            workspace.Items[0] == expected, "Rename must publish complete presentation and clear rename before Changed.");
                    };
                    ExplorerRenameResult result = workspace.CommitRename(test.Name);
                    returned = true;
                    Require(result == new ExplorerRenameResult(true, ExplorerRenameError.None, before.Id, test.Name, "Rename committed.") &&
                        notifications == 1 && workspace.OpenSelected().PreviewRequest ==
                        new ExplorerPreviewRequest(before.Id, test.Name, test.Type, test.Preview,
                            "JARVIS2.NEURAL_VOID_PREVIEW", true, false, false),
                        $"Dot-prefix rename contract changed for '{test.Name}'.");
                }
                return true;
            });

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

    private static bool CompleteSeedContract()
    {
        (ExplorerWorkspaceItem Item, string? ParentId, string PreviewKind)[] expected =
        [
            (new("location:this-pc", "THIS PC", ExplorerWorkspaceItemKind.Folder, "ROOT", "this-pc", null,
                new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero), false, false, false), null, ""),
            (new("location:neural-storage", "NEURAL STORAGE", ExplorerWorkspaceItemKind.Folder, "STORAGE", "neural-storage", null,
                new(2026, 8, 1, 9, 1, 0, TimeSpan.Zero), false, false, false), "location:this-pc", ""),
            (new("location:documents", "DOCUMENTS", ExplorerWorkspaceItemKind.Folder, "FOLDER", "documents", null,
                new(2026, 8, 4, 13, 42, 0, TimeSpan.Zero), false, false, false), "location:neural-storage", ""),
            (new("location:projects", "PROJECTS", ExplorerWorkspaceItemKind.Folder, "FOLDER", "projects", null,
                new(2026, 8, 3, 18, 20, 0, TimeSpan.Zero), false, false, false), "location:neural-storage", ""),
            (new("location:archive", "ARCHIVE", ExplorerWorkspaceItemKind.Folder, "FOLDER", "archive", null,
                new(2026, 7, 29, 16, 18, 0, TimeSpan.Zero), false, false, false), "location:neural-storage", ""),
            (new("file:project-brief-0826", "PROJECT_BRIEF_0826.DOCX", ExplorerWorkspaceItemKind.File, "DOCX", "document", 2_400_000,
                new(2026, 8, 4, 10, 21, 0, TimeSpan.Zero), false, false, true), "location:documents", "document-outline"),
            (new("file:specifications-v2", "SPECIFICATIONS_V2.PDF", ExplorerWorkspaceItemKind.File, "PDF", "pdf", 1_800_000,
                new(2026, 8, 3, 15, 42, 0, TimeSpan.Zero), false, false, true), "location:documents", "pdf-outline"),
            (new("file:report-draft", "REPORT_DRAFT.PPTX", ExplorerWorkspaceItemKind.File, "PPTX", "presentation", 5_700_000,
                new(2026, 8, 2, 11, 9, 0, TimeSpan.Zero), false, false, true), "location:documents", "presentation-outline"),
            (new("file:data-sheet", "DATA_SHEET.XLSX", ExplorerWorkspaceItemKind.File, "XLSX", "spreadsheet", 1_200_000,
                new(2026, 8, 1, 9, 33, 0, TimeSpan.Zero), false, false, true), "location:documents", "spreadsheet-outline"),
            (new("file:meeting-notes", "MEETING_NOTES_0804.TXT", ExplorerWorkspaceItemKind.File, "TXT", "text", 18_000,
                new(2026, 8, 4, 8, 47, 0, TimeSpan.Zero), false, false, true), "location:documents", "text-outline"),
            (new("file:site-map", "SITE_MAP.VIS", ExplorerWorkspaceItemKind.File, "VISUAL", "visual", 640_000,
                new(2026, 8, 2, 17, 26, 0, TimeSpan.Zero), false, false, true), "location:documents", "visual-outline"),
            (new("folder:aurora-engine", "AURORA ENGINE", ExplorerWorkspaceItemKind.Folder, "FOLDER", "project-folder", null,
                new(2026, 8, 4, 12, 4, 0, TimeSpan.Zero), false, false, false), "location:projects", ""),
            (new("file:horizon-research-matrix", "HORIZON_MEMBRANE_SPATIAL_COMPUTE_RESEARCH_NOTES_" +
                "AND_IMPLEMENTATION_MATRIX_2026-08-04.MD", ExplorerWorkspaceItemKind.File, "MD", "text", 84_000,
                new(2026, 8, 4, 12, 28, 0, TimeSpan.Zero), false, false, true), "location:projects", "text-outline"),
        ];
        Dictionary<string, ExplorerBreadcrumbSegment[]> breadcrumbs = new(StringComparer.Ordinal)
        {
            ["location:this-pc"] = [new("location:this-pc", "THIS PC")],
            ["location:neural-storage"] = [new("location:this-pc", "THIS PC"), new("location:neural-storage", "NEURAL STORAGE")],
            ["location:documents"] = [new("location:this-pc", "THIS PC"), new("location:neural-storage", "NEURAL STORAGE"), new("location:documents", "DOCUMENTS")],
            ["location:projects"] = [new("location:this-pc", "THIS PC"), new("location:neural-storage", "NEURAL STORAGE"), new("location:projects", "PROJECTS")],
            ["location:archive"] = [new("location:this-pc", "THIS PC"), new("location:neural-storage", "NEURAL STORAGE"), new("location:archive", "ARCHIVE")],
            ["folder:aurora-engine"] = [new("location:this-pc", "THIS PC"), new("location:neural-storage", "NEURAL STORAGE"), new("location:projects", "PROJECTS"), new("folder:aurora-engine", "AURORA ENGINE")],
        };
        ExplorerWorkspace seeded = new();
        Require(seeded.CurrentLocationId == "location:documents", "Initial location must remain Documents.");
        System.Collections.IDictionary nodes = (System.Collections.IDictionary)(typeof(ExplorerWorkspace)
            .GetField("_nodes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(seeded) ?? throw new InvalidOperationException("Seed dictionary absent."));
        Require(nodes.Keys.Cast<string>().SequenceEqual(expected.Select(row => row.Item.Id)), "Seed insertion order changed.");
        foreach (var row in expected)
        {
            Require(seeded.TryGetItem(row.Item.Id, out ExplorerWorkspaceItem? item) && item == row.Item,
                $"Complete seed fields changed: {row.Item.Id}.");
            object node = nodes[row.Item.Id]!;
            Require(Equals(node.GetType().GetProperty("ParentId")!.GetValue(node), row.ParentId) &&
                Equals(node.GetType().GetProperty("PreviewKind")!.GetValue(node), row.PreviewKind),
                $"Seed parent or preview changed: {row.Item.Id}.");
            if (row.Item.Kind == ExplorerWorkspaceItemKind.Folder)
            {
                ExplorerWorkspace workspace = new();
                _ = workspace.TryNavigateTo(row.Item.Id);
                ExplorerWorkspaceSnapshot snapshot = workspace.CaptureSnapshot();
                ExplorerWorkspaceItem[] children = expected.Where(child => child.ParentId == row.Item.Id).Select(child => child.Item).ToArray();
                Require(snapshot.CurrentLocation == new ExplorerWorkspaceLocation(row.Item.Id, row.Item.Name, row.Item.IconKey) &&
                    snapshot.Breadcrumb.SequenceEqual(breadcrumbs[row.Item.Id]) && snapshot.Items.SequenceEqual(children) &&
                    snapshot.SelectedItemId is null && snapshot.RenameItemId is null &&
                    snapshot.CanGoBack == (row.Item.Id != "location:documents") && !snapshot.CanGoForward &&
                    snapshot.CanGoUp == (row.ParentId is not null) && snapshot.IsEmpty == (children.Length == 0),
                    $"Complete location snapshot changed: {row.Item.Id}.");
            }
            else
            {
                ExplorerWorkspace workspace = new();
                _ = workspace.TryNavigateTo(row.ParentId);
                ExplorerOpenResult result = workspace.OpenItem(row.Item.Id);
                Require(result == new ExplorerOpenResult(ExplorerOpenDisposition.PreviewRequested, row.ParentId!,
                    new(row.Item.Id, row.Item.Name, row.Item.TypeLabel, row.PreviewKind,
                        "JARVIS2.NEURAL_VOID_PREVIEW", true, false, false)), $"Complete preview contract changed: {row.Item.Id}.");
                Require(workspace.TryGetItem(row.Item.Id, out ExplorerWorkspaceItem? selected) &&
                    selected == row.Item with { IsSelected = true }, "File-open selection or presentation changed.");
            }
        }
        return true;
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
