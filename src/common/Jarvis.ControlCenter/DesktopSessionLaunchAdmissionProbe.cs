using System.IO;

namespace Jarvis.ControlCenter;

public sealed record DesktopSessionLaunchAdmissionProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    bool WorkspaceAdmissionPassed,
    bool LocalLaunchPassed,
    bool OpenAiLaunchPassed,
    bool IncompleteRuntimeRejected,
    bool RelativeWorkspaceRejected,
    bool MissingWorkspaceRejected,
    bool DriveRootRejected,
    bool ProtectedWorkspaceRejected,
    bool UnknownProviderRejected,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

public sealed record DesktopSessionLaunchLifecycleProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    bool IdleLaunchAvailable,
    bool RuntimeReady,
    bool RuntimeStopped,
    bool OwnedRuntimeReleased,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

public static class DesktopSessionLaunchAdmissionProbe
{
    public static DesktopSessionLaunchAdmissionProbeReceipt Run(
        string repositoryRoot,
        string nodeExecutablePath)
    {
        List<string> failures = [];
        string workspace = Path.GetFullPath(repositoryRoot);
        string node = Path.GetFullPath(nodeExecutablePath);

        DesktopWorkspaceAdmissionReceipt workspaceAdmission =
            DesktopSessionLaunchAdmission.AdmitWorkspace(workspace);
        DesktopSessionLaunchAdmissionReceipt local =
            DesktopSessionLaunchAdmission.Admit(
                workspace,
                ConversationProviderKind.LocalDiagnostic,
                workspace,
                pathEnvironment: string.Empty,
                configuredNodePath: node);
        DesktopSessionLaunchAdmissionReceipt openAi =
            DesktopSessionLaunchAdmission.Admit(
                workspace,
                ConversationProviderKind.OpenAiResponses,
                workspace,
                pathEnvironment: string.Empty,
                configuredNodePath: node);
        DesktopWorkspaceAdmissionReceipt relative =
            DesktopSessionLaunchAdmission.AdmitWorkspace(".");
        DesktopWorkspaceAdmissionReceipt missing =
            DesktopSessionLaunchAdmission.AdmitWorkspace(
                Path.Combine(
                    workspace,
                    $".jarvis2-missing-{Guid.NewGuid():N}"));
        string volumeRoot = Path.GetPathRoot(workspace) ?? workspace;
        DesktopWorkspaceAdmissionReceipt drive =
            DesktopSessionLaunchAdmission.AdmitWorkspace(volumeRoot);
        string protectedRoot = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        DesktopWorkspaceAdmissionReceipt protectedWorkspace =
            DesktopSessionLaunchAdmission.AdmitWorkspace(protectedRoot);
        DesktopSessionLaunchAdmissionReceipt unknownProvider =
            DesktopSessionLaunchAdmission.Admit(
                workspace,
                (ConversationProviderKind)(-1),
                workspace,
                pathEnvironment: string.Empty,
                configuredNodePath: node);

        bool workspaceAdmissionPassed =
            workspaceAdmission.Result == "passed" &&
            workspaceAdmission.WorkspaceRoot == workspace;
        bool localLaunchPassed =
            local.Result == "passed" &&
            local.Options?.Provider ==
                ConversationProviderKind.LocalDiagnostic &&
            local.Options.WorkspaceRoot == workspace;
        bool openAiLaunchPassed =
            openAi.Result == "passed" &&
            openAi.Options?.Provider ==
                ConversationProviderKind.OpenAiResponses &&
            openAi.Options.WorkspaceRoot == workspace;
        bool incompleteRuntimeRejected =
            local.Result == "failed" &&
            local.Options is null &&
            local.Failures.Count != 0 &&
            openAi.Result == "failed" &&
            openAi.Options is null &&
            openAi.Failures.Count != 0;
        bool relativeWorkspaceRejected =
            relative.Result == "failed" &&
            relative.FailureCode == "invalid-workspace-root";
        bool missingWorkspaceRejected =
            missing.Result == "failed" &&
            missing.FailureCode == "workspace-root-not-found";
        bool driveRootRejected =
            drive.Result == "failed" &&
            drive.FailureCode == "protected-workspace-root";
        bool protectedWorkspaceRejected =
            protectedWorkspace.Result == "failed" &&
            protectedWorkspace.FailureCode == "protected-workspace-root";
        bool unknownProviderRejected = unknownProvider.Result == "failed";

        AddFailure(
            failures,
            workspaceAdmissionPassed,
            "The repository workspace did not pass desktop admission.");
        AddFailure(
            failures,
            (localLaunchPassed && openAiLaunchPassed) ||
                incompleteRuntimeRejected,
            "The provider launch boundary neither resolved a complete " +
                "runtime nor rejected an incomplete runtime.");
        AddFailure(
            failures,
            relativeWorkspaceRejected,
            "A relative workspace was not rejected.");
        AddFailure(
            failures,
            missingWorkspaceRejected,
            "A missing workspace was not rejected.");
        AddFailure(
            failures,
            driveRootRejected,
            "A drive root was not rejected.");
        AddFailure(
            failures,
            protectedWorkspaceRejected,
            "An application-data workspace was not rejected.");
        AddFailure(
            failures,
            unknownProviderRejected,
            "An unknown provider was not rejected.");

        return new DesktopSessionLaunchAdmissionProbeReceipt(
            1,
            "jarvisv2-desktop-session-launch-admission-probe",
            failures.Count == 0 ? "passed" : "failed",
            workspaceAdmissionPassed,
            localLaunchPassed,
            openAiLaunchPassed,
            incompleteRuntimeRejected,
            relativeWorkspaceRejected,
            missingWorkspaceRejected,
            driveRootRejected,
            protectedWorkspaceRejected,
            unknownProviderRejected,
            false,
            failures);
    }

    public static async Task<DesktopSessionLaunchLifecycleProbeReceipt>
        RunLifecycleAsync(
            string repositoryRoot,
            string nodeExecutablePath,
            CancellationToken cancellationToken = default)
    {
        List<string> failures = [];
        string workspace = Path.GetFullPath(repositoryRoot);
        string node = Path.GetFullPath(nodeExecutablePath);
        DesktopSessionLaunchAdmissionReceipt admission =
            DesktopSessionLaunchAdmission.Admit(
                workspace,
                ConversationProviderKind.LocalDiagnostic,
                workspace,
                pathEnvironment: string.Empty,
                configuredNodePath: node);
        if (admission.Result != "passed" || admission.Options is null)
        {
            failures.AddRange(admission.Failures);
            return LifecycleReceipt(false, false, false, true, failures);
        }

        ConversationSurfaceViewModel viewModel =
            ConversationSurfaceViewModel.CreateIdle();
        bool idleLaunchAvailable =
            viewModel.Phase == ConversationRuntimePhase.NotStarted &&
            viewModel.CanLaunchSession &&
            !viewModel.HasOwnedRuntime;
        bool runtimeReady = false;
        bool runtimeStopped = false;
        try
        {
            await viewModel.LaunchAsync(
                admission.Options,
                cancellationToken);
            runtimeReady =
                viewModel.Phase == ConversationRuntimePhase.Ready &&
                viewModel.HasOwnedRuntime &&
                !viewModel.CanLaunchSession;
            await viewModel.ShutdownAsync(cancellationToken);
            runtimeStopped =
                viewModel.Phase == ConversationRuntimePhase.Stopped &&
                !viewModel.HasOwnedRuntime;
        }
        catch (Exception exception)
        {
            failures.Add($"In-process session lifecycle failed: {exception.Message}");
        }
        finally
        {
            await viewModel.DisposeAsync();
        }

        bool ownedRuntimeReleased = !viewModel.HasOwnedRuntime;
        AddFailure(
            failures,
            idleLaunchAvailable,
            "The idle surface did not offer an in-app session launch.");
        AddFailure(
            failures,
            runtimeReady,
            "The admitted local Pi session did not reach Ready.");
        AddFailure(
            failures,
            runtimeStopped,
            "The admitted local Pi session did not reach orderly Stopped.");
        AddFailure(
            failures,
            ownedRuntimeReleased,
            "The local Pi runtime remained owned after shutdown.");
        return LifecycleReceipt(
            idleLaunchAvailable,
            runtimeReady,
            runtimeStopped,
            ownedRuntimeReleased,
            failures);
    }

    private static DesktopSessionLaunchLifecycleProbeReceipt LifecycleReceipt(
        bool idleLaunchAvailable,
        bool runtimeReady,
        bool runtimeStopped,
        bool ownedRuntimeReleased,
        IReadOnlyList<string> failures) =>
        new(
            1,
            "jarvisv2-desktop-session-launch-lifecycle-probe",
            failures.Count == 0 ? "passed" : "failed",
            idleLaunchAvailable,
            runtimeReady,
            runtimeStopped,
            ownedRuntimeReleased,
            false,
            failures);

    private static void AddFailure(
        ICollection<string> failures,
        bool passed,
        string failure)
    {
        if (!passed)
        {
            failures.Add(failure);
        }
    }
}
