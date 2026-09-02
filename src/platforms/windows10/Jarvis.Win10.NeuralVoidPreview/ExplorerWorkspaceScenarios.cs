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
