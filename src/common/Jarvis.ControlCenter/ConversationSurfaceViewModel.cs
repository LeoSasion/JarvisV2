using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Jarvis.PiAgentHost;

namespace Jarvis.ControlCenter;

public enum ConversationRuntimePhase
{
    NotStarted,
    Preview,
    Starting,
    Ready,
    Stopping,
    Stopped,
    Faulted,
}

public sealed class ConversationSurfaceViewModel :
    INotifyPropertyChanged,
    IAsyncDisposable
{
    private static readonly PiAgentConversationSnapshot EmptySnapshot =
        new(0, null, false, false, []);

    private ConversationLaunchOptions? launchOptions;
    private readonly OpenAiApiKeyCredentialStore credentialStore = new();
    private readonly bool preview;
    private PiAgentDesktopRuntime? runtime;
    private PiAgentConversationBinding? binding;
    private PiAgentReviewedIterationCoordinator? reviewedIteration;
    private PiAgentReviewedIterationSnapshot? reviewedIterationSnapshot;
    private PiAgentConversationSnapshot snapshot = EmptySnapshot;
    private ConversationRuntimePhase phase;
    private string statusDetail;
    private string? uiError;
    private bool providerCredentialReady;
    private int disposeStarted;

    private ConversationSurfaceViewModel(
        ConversationLaunchOptions? launchOptions,
        bool preview)
    {
        this.launchOptions = launchOptions;
        this.preview = preview;
        phase = preview
            ? ConversationRuntimePhase.Preview
            : ConversationRuntimePhase.NotStarted;
        statusDetail = preview
            ? UiText.Get("Loc.Runtime.Status.Preview")
            : UiText.Get("Loc.Runtime.Status.Idle");
        if (preview)
        {
            snapshot = CreatePreviewSnapshot();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ConversationSurfaceViewModel CreateIdle() =>
        new(null, preview: false);

    public static ConversationSurfaceViewModel CreatePreview() =>
        new(null, preview: true);

    public static ConversationSurfaceViewModel Create(
        ConversationLaunchOptions options) =>
        new(
            options ?? throw new ArgumentNullException(nameof(options)),
            preview: false);

    public ConversationRuntimePhase Phase => phase;
    public string PhaseLabel => phase switch
    {
        ConversationRuntimePhase.NotStarted => UiText.Get("Loc.Runtime.Phase.NotStarted"),
        ConversationRuntimePhase.Preview => UiText.Get("Loc.Runtime.Phase.Preview"),
        ConversationRuntimePhase.Starting => UiText.Get("Loc.Runtime.Phase.Starting"),
        ConversationRuntimePhase.Ready => UiText.Get("Loc.Runtime.Phase.Ready"),
        ConversationRuntimePhase.Stopping => UiText.Get("Loc.Runtime.Phase.Stopping"),
        ConversationRuntimePhase.Stopped => UiText.Get("Loc.Runtime.Phase.Stopped"),
        ConversationRuntimePhase.Faulted => UiText.Get("Loc.Runtime.Phase.Faulted"),
        _ => UiText.Get("Loc.Runtime.Phase.Unknown"),
    };
    public string StatusDetail => uiError ?? statusDetail;
    public string ProviderLabel => preview
        ? UiText.Get("Loc.Runtime.Provider.Preview")
        : launchOptions?.ProviderDisplayName ?? UiText.Get("Loc.Runtime.Provider.None");
    public string AccessLabel => UiText.Get("Loc.Runtime.Access");
    public string WorkspaceLabel => launchOptions?.WorkspaceRoot ??
        (preview
            ? UiText.Get("Loc.Runtime.Workspace.Preview")
            : UiText.Get("Loc.Runtime.Workspace.None"));
    public string CheckpointLabel => runtime is null
        ? preview
            ? UiText.Get("Loc.Runtime.Checkpoint.Preview")
            : UiText.Get("Loc.Runtime.Checkpoint.NotLoaded")
        : runtime.CheckpointPersistenceFaulted
            ? UiText.Get("Loc.Runtime.Checkpoint.Faulted")
            : UiText.Format(
                "Loc.Runtime.Checkpoint.Counts",
                runtime.CheckpointSaveCount,
                runtime.RestoredCheckpointTurnCount);
    public string CredentialLabel => runtime?.CredentialEnvironmentClean == true
        ? launchOptions?.Provider == ConversationProviderKind.OpenAiResponses
            ? providerCredentialReady
                ? "DESKTOP DPAPI READY // SIDECAR CLEAN"
                : "OPENAI AUTH REQUIRED // SIDECAR CLEAN"
            : providerCredentialReady
                ? "OPENAI STORED // LOCAL PROVIDER ACTIVE"
                : "SIDECAR CLEAN // OPENAI OPTIONAL"
        : preview
            ? UiText.Get("Loc.Runtime.Credential.Preview")
            : UiText.Get("Loc.Runtime.Credential.NotReady");
    public string BrokerLabel => runtime is null
        ? preview
            ? UiText.Get("Loc.Runtime.Broker.Preview")
            : UiText.Get("Loc.Runtime.Broker.None")
        : UiText.Format(
            "Loc.Runtime.Broker.Counts",
            runtime.BrokerRequestCount,
            runtime.BrokerFaultCount);
    public string ShutdownLabel => phase switch
    {
        ConversationRuntimePhase.Stopping => UiText.Get("Loc.Runtime.Shutdown.Stopping"),
        ConversationRuntimePhase.Stopped => UiText.Get("Loc.Runtime.Shutdown.Stopped"),
        ConversationRuntimePhase.Ready => UiText.Get("Loc.Runtime.Shutdown.Ready"),
        _ => preview
            ? UiText.Get("Loc.Runtime.Shutdown.Preview")
            : UiText.Get("Loc.Runtime.Shutdown.None"),
    };
    public IReadOnlyList<PiAgentConversationTurnSnapshot> Turns =>
        snapshot.Turns;
    public IReadOnlyList<PiAgentConversationToolSnapshot> ActiveTools =>
        ActiveTurn?.Tools ?? [];
    public PiAgentWorkspaceEditSnapshot? PendingWorkspaceEdit =>
        snapshot.Turns
            .SelectMany(turn => turn.WorkspaceEdits)
            .SingleOrDefault(edit =>
                edit.Status == PiAgentWorkspaceEditStatus.Pending);
    public bool HasTurns => snapshot.Turns.Count != 0;
    public bool HasActiveTurn => snapshot.ActiveTurnId is not null;
    public bool HandoffComplete =>
        snapshot.ActiveTurnId is null && snapshot.Turns.Count != 0;
    public bool CanSubmit =>
        phase == ConversationRuntimePhase.Ready &&
        snapshot.CanSubmit &&
        (reviewedIterationSnapshot is null ||
            reviewedIterationSnapshot.IsTerminal) &&
        (launchOptions?.Provider != ConversationProviderKind.OpenAiResponses ||
            providerCredentialReady);
    public bool CanCancel =>
        phase == ConversationRuntimePhase.Ready && snapshot.CanCancel;
    public bool CanReviewWorkspaceEdits =>
        phase == ConversationRuntimePhase.Ready &&
        snapshot.ActiveTurnId is null &&
        PendingWorkspaceEdit?.CanDecide == true &&
        (
            reviewedIterationSnapshot is null ||
            reviewedIterationSnapshot.IsTerminal ||
            reviewedIterationSnapshot.Status ==
                PiAgentReviewedIterationStatus.AwaitingOwnerReview
        );
    public bool CanLaunchSession =>
        !preview &&
        runtime is null &&
        phase is ConversationRuntimePhase.NotStarted or
            ConversationRuntimePhase.Faulted;
    public bool HasOwnedRuntime => runtime is not null && !runtime.IsShutdown;
    public PiAgentReviewedIterationSnapshot? ReviewedIteration =>
        reviewedIterationSnapshot;
    public bool HasReviewedIteration => reviewedIterationSnapshot is not null;
    public bool CanStartReviewedIteration =>
        phase == ConversationRuntimePhase.Ready &&
        snapshot.CanSubmit &&
        (reviewedIterationSnapshot is null ||
            reviewedIterationSnapshot.IsTerminal) &&
        (launchOptions?.Provider != ConversationProviderKind.OpenAiResponses ||
            providerCredentialReady);
    public bool CanResumeReviewedIteration =>
        phase == ConversationRuntimePhase.Ready &&
        snapshot.CanSubmit &&
        reviewedIterationSnapshot?.Status ==
            PiAgentReviewedIterationStatus.Interrupted &&
        (launchOptions?.Provider != ConversationProviderKind.OpenAiResponses ||
            providerCredentialReady);
    public bool CanRunTrustedValidation =>
        phase == ConversationRuntimePhase.Ready &&
        snapshot.CanSubmit &&
        reviewedIterationSnapshot?.Status ==
            PiAgentReviewedIterationStatus.AwaitingTrustedValidation &&
        (launchOptions?.Provider != ConversationProviderKind.OpenAiResponses ||
            providerCredentialReady);
    public bool CanStopReviewedIteration =>
        phase == ConversationRuntimePhase.Ready &&
        reviewedIterationSnapshot is { IsTerminal: false };
    public string ReviewedIterationStatusLabel =>
        reviewedIterationSnapshot?.StatusLabel ??
        UiText.Get("Loc.Runtime.Review.NotArmed");
    public string ReviewedIterationDetail =>
        reviewedIterationSnapshot?.StatusDetail ??
        UiText.Get("Loc.Runtime.Review.Detail");
    public string ReviewedIterationProgressLabel =>
        reviewedIterationSnapshot?.ProgressLabel ??
        UiText.Get("Loc.Runtime.Review.Progress");
    public string ReviewedIterationReceiptLabel =>
        reviewedIterationSnapshot?.ReceiptLabel ??
        UiText.Get("Loc.Runtime.Review.Receipt");
    public string ReviewedIterationHeadLabel =>
        reviewedIterationSnapshot?.HeadLabel ??
        UiText.Get("Loc.Runtime.Review.Head");
    public string ReviewedIterationExpiryLabel =>
        reviewedIterationSnapshot?.ExpiryLabel ??
        UiText.Get("Loc.Runtime.Review.Expiry");
    public string ReviewedIterationValidationProfileLabel =>
        reviewedIterationSnapshot?.TrustedValidationProfileId is string profile
            ? UiText.Format("Loc.Runtime.Review.ProfileValue", profile)
            : UiText.Get("Loc.Runtime.Review.Profile");
    public string ReviewedIterationValidationCommand =>
        reviewedIterationSnapshot?.TrustedValidationCommand ??
        UiText.Get("Loc.Runtime.Review.Command");
    public bool IsOpenAiProvider =>
        launchOptions?.Provider == ConversationProviderKind.OpenAiResponses;
    public double HandoffProgress => DetermineHandoffProgress();
    public string HandoffLabel => HandoffProgress switch
    {
        _ when PendingWorkspaceEdit is not null =>
            UiText.Get("Loc.Runtime.Handoff.Owner"),
        <= 0 => UiText.Get("Loc.Runtime.Handoff.User"),
        < 2 => UiText.Get("Loc.Runtime.Handoff.Pi"),
        < 3 => UiText.Get("Loc.Runtime.Handoff.Tool"),
        _ => snapshot.ActiveTurnId is null
            ? UiText.Get("Loc.Runtime.Handoff.Complete")
            : UiText.Get("Loc.Runtime.Handoff.Streaming"),
    };
    public string EmptyStateTitle => phase switch
    {
        ConversationRuntimePhase.NotStarted when launchOptions is null =>
            "Start a workspace session",
        ConversationRuntimePhase.Starting =>
            "Pi is admitting the workspace",
        ConversationRuntimePhase.Ready when
            IsOpenAiProvider && !providerCredentialReady =>
            "OpenAI authentication is required",
        ConversationRuntimePhase.Ready =>
            "Pi is ready for your first request",
        ConversationRuntimePhase.Faulted =>
            "Runtime admission needs attention",
        ConversationRuntimePhase.Preview =>
            "Illustrative handoff complete",
        _ => "No turn has been handed to Pi",
    };
    public string EmptyStateDescription => phase switch
    {
        ConversationRuntimePhase.NotStarted when launchOptions is null =>
            "Choose one workspace and start the local review-gated Pi runtime. " +
                "No command line is required.",
        ConversationRuntimePhase.Starting =>
            "The desktop is verifying the runtime, workspace and broker boundary.",
        ConversationRuntimePhase.Ready when
            IsOpenAiProvider && !providerCredentialReady =>
            "Configure the protected OpenAI key in the inspector, then submit " +
                "your first request.",
        ConversationRuntimePhase.Ready =>
            "Submit a request below. Reads are root-confined; exact replacements and new UTF-8 files can only be staged for your explicit review.",
        ConversationRuntimePhase.Faulted =>
            "Review the status detail, then choose a different workspace or " +
                "repair the portable runtime.",
        ConversationRuntimePhase.Preview =>
            "Preview data only; no runtime, workspace or tool was started.",
        _ => "Start another admitted session to continue.",
    };
    public string SessionLaunchActionLabel => phase switch
    {
        ConversationRuntimePhase.Faulted => "CHOOSE ANOTHER SESSION",
        ConversationRuntimePhase.Ready => "SESSION READY",
        ConversationRuntimePhase.Starting => "ADMITTING SESSION",
        ConversationRuntimePhase.Preview => "PREVIEW ONLY",
        _ => "START PI SESSION",
    };

    private PiAgentConversationTurnSnapshot? ActiveTurn =>
        snapshot.ActiveTurnId is null
            ? null
            : snapshot.Turns.LastOrDefault(turn =>
                string.Equals(
                    turn.TurnId,
                    snapshot.ActiveTurnId,
                    StringComparison.Ordinal));

    public async Task LaunchAsync(
        ConversationLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!CanLaunchSession)
        {
            throw new InvalidOperationException(
                "A new Pi session cannot be launched in the current runtime phase.");
        }
        launchOptions = options;
        snapshot = EmptySnapshot;
        reviewedIterationSnapshot = null;
        providerCredentialReady = false;
        phase = ConversationRuntimePhase.NotStarted;
        statusDetail = "Workspace selected. Starting the desktop-owned Pi runtime.";
        uiError = null;
        RaiseRuntimeProperties();
        RaiseConversationProperties();
        await InitializeAsync(cancellationToken);
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (preview || launchOptions is null || phase != ConversationRuntimePhase.NotStarted)
        {
            return;
        }

        SetPhase(
            ConversationRuntimePhase.Starting,
            "Admitting workspace, broker, sidecar and Pi session.");
        try
        {
            PiAgentSidecarOptions sidecar = new(
                Path.GetFullPath(launchOptions.NodeExecutablePath),
                Path.GetFullPath(launchOptions.SidecarHostPath));
            IDesktopModelProvider provider;
            if (launchOptions.Provider == ConversationProviderKind.OpenAiResponses)
            {
                providerCredentialReady =
                    await credentialStore.GetApiKeyAsync(cancellationToken) is not null;
                provider = new OpenAiResponsesModelProvider(credentialStore);
            }
            else
            {
                providerCredentialReady = false;
                provider = new LocalDiagnosticModelProvider();
            }
            runtime = await PiAgentDesktopRuntime.StartAsync(
                new PiAgentDesktopRuntimeOptions(
                    sidecar,
                    Path.GetFullPath(launchOptions.WorkspaceRoot),
                    ConversationCheckpointStore:
                        new PiAgentConversationCheckpointStore()),
                provider,
                SynchronizationContext.Current,
                cancellationToken);
            binding = new PiAgentConversationBinding(runtime.Conversation);
            binding.PropertyChanged += OnBindingPropertyChanged;
            snapshot = binding.Snapshot;
            reviewedIteration =
                await PiAgentReviewedIterationCoordinator.OpenAsync(
                    runtime.Conversation,
                    runtime.WorkspaceRoot,
                    sidecar,
                    cancellationToken: cancellationToken);
            reviewedIteration.SnapshotChanged +=
                OnReviewedIterationSnapshotChanged;
            reviewedIterationSnapshot = reviewedIteration.Snapshot;
            SetPhase(
                ConversationRuntimePhase.Ready,
                IsOpenAiProvider && !providerCredentialReady
                    ? "Pi is ready. Configure OpenAI to enable authenticated turns."
                    : "Pi session admitted. Reads are root-confined and " +
                        "workspace write proposals require a one-shot owner decision.");
            RaiseConversationProperties();
            RaiseReviewedIterationProperties();
        }
        catch (Exception exception)
        {
            SetPhase(
                ConversationRuntimePhase.Faulted,
                $"Runtime admission failed: {exception.Message}");
            if (reviewedIteration is not null)
            {
                reviewedIteration.SnapshotChanged -=
                    OnReviewedIterationSnapshotChanged;
                reviewedIteration = null;
            }
            reviewedIterationSnapshot = null;
            if (binding is not null)
            {
                binding.PropertyChanged -= OnBindingPropertyChanged;
                binding.Dispose();
                binding = null;
            }
            if (runtime is not null)
            {
                await runtime.DisposeAsync();
                runtime = null;
            }
            RaiseConversationProperties();
            RaiseReviewedIterationProperties();
        }
    }

    public async Task RefreshCredentialAsync(
        CancellationToken cancellationToken = default)
    {
        providerCredentialReady =
            await credentialStore.GetApiKeyAsync(cancellationToken) is not null;
        if (IsOpenAiProvider && phase == ConversationRuntimePhase.Ready)
        {
            statusDetail = providerCredentialReady
                ? "OpenAI credential admitted by the desktop. Pi remains credential-free."
                : "Configure OpenAI to enable authenticated turns.";
        }
        else if (phase == ConversationRuntimePhase.Ready && providerCredentialReady)
        {
            statusDetail =
                "OpenAI credential protected. Relaunch with --provider openai to use it.";
        }
        RaisePropertyChanged(nameof(CredentialLabel));
        RaisePropertyChanged(nameof(CanSubmit));
        RaisePropertyChanged(nameof(StatusDetail));
    }

    public OpenAiApiKeyCredentialStore CredentialStore => credentialStore;

    public async Task SubmitAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        uiError = null;
        if (!CanSubmit || binding is null)
        {
            throw new InvalidOperationException(
                "The Pi conversation is not ready for a new turn.");
        }

        PiAgentConversationTurn turn = await binding.SubmitAsync(
            text,
            cancellationToken);
        statusDetail = "Turn admitted. Ownership is moving through Pi.";
        RaisePropertyChanged(nameof(StatusDetail));
        _ = ObserveCompletionAsync(turn);
    }

    public async Task CancelAsync(
        CancellationToken cancellationToken = default)
    {
        uiError = null;
        if (binding is null || !CanCancel)
        {
            return;
        }

        bool accepted = await binding.CancelAsync(cancellationToken);
        statusDetail = accepted
            ? "Cancellation requested; waiting for the terminal turn event."
            : "The active turn had already reached a terminal state.";
        RaisePropertyChanged(nameof(StatusDetail));
    }

    public async Task StartReviewedIterationAsync(
        string mission,
        CancellationToken cancellationToken = default)
    {
        uiError = null;
        if (reviewedIteration is null || !CanStartReviewedIteration)
        {
            throw new InvalidOperationException(
                "The reviewed iteration policy cannot be armed in the current state.");
        }
        PiAgentConversationTurn turn =
            await reviewedIteration.StartAsync(
                mission,
                cancellationToken);
        statusDetail =
            "Reviewed iteration armed from a clean Git HEAD. Pi may stage one edit for owner review.";
        RaisePropertyChanged(nameof(StatusDetail));
        RaiseReviewedIterationProperties();
        _ = ObserveReviewedIterationCompletionAsync(turn);
    }

    public async Task ResumeReviewedIterationAsync(
        CancellationToken cancellationToken = default)
    {
        uiError = null;
        if (reviewedIteration is null || !CanResumeReviewedIteration)
        {
            throw new InvalidOperationException(
                "The reviewed iteration is not ready for explicit re-arm.");
        }
        PiAgentConversationTurn? turn =
            await reviewedIteration.ResumeAsync(cancellationToken);
        statusDetail = turn is null
            ? "Durable receipts revalidated. The pending fixed test run still requires its separate owner approval."
            : "Durable receipts and repository state revalidated. One bounded continuation was re-armed.";
        RaisePropertyChanged(nameof(StatusDetail));
        RaiseReviewedIterationProperties();
        if (turn is not null)
        {
            _ = ObserveReviewedIterationCompletionAsync(turn);
        }
    }

    public async Task RunTrustedValidationAsync(
        CancellationToken cancellationToken = default)
    {
        uiError = null;
        if (reviewedIteration is null || !CanRunTrustedValidation)
        {
            throw new InvalidOperationException(
                "The reviewed iteration is not awaiting trusted validation approval.");
        }
        PiAgentTrustedValidationDecisionResult result =
            await reviewedIteration.RunTrustedValidationAndContinueAsync(
                cancellationToken);
        statusDetail = result.Validation?.Passed == true
            ? "Pinned trusted tests passed once and the repository remained exact."
            : result.Iteration.StatusDetail;
        RaisePropertyChanged(nameof(StatusDetail));
        RaiseConversationProperties();
        RaiseReviewedIterationProperties();
        if (result.ContinuedTurn is not null)
        {
            _ = ObserveReviewedIterationCompletionAsync(
                result.ContinuedTurn);
        }
    }

    public async Task StopReviewedIterationAsync(
        CancellationToken cancellationToken = default)
    {
        uiError = null;
        if (reviewedIteration is null || !CanStopReviewedIteration)
        {
            return;
        }
        await reviewedIteration.StopAsync(cancellationToken);
        statusDetail =
            "Reviewed iteration stopped by the owner. No continuation is admitted.";
        RaisePropertyChanged(nameof(StatusDetail));
        RaiseReviewedIterationProperties();
    }

    public async Task ApplyWorkspaceEditAsync(
        string proposalId,
        CancellationToken cancellationToken = default)
    {
        uiError = null;
        if (binding is null || !CanReviewWorkspaceEdits)
        {
            throw new InvalidOperationException(
                "The workspace proposal is not ready for approval.");
        }
        PiAgentWorkspaceEditSnapshot result;
        if (
            reviewedIteration is not null &&
            reviewedIterationSnapshot?.CurrentProposalId == proposalId)
        {
            PiAgentReviewedIterationDecisionResult decision =
                await reviewedIteration.ApproveAndContinueAsync(
                    proposalId,
                    cancellationToken);
            result = decision.Edit;
            if (decision.ContinuedTurn is not null)
            {
                _ = ObserveReviewedIterationCompletionAsync(
                    decision.ContinuedTurn);
            }
        }
        else
        {
            result = await binding.ApplyWorkspaceEditAsync(
                proposalId,
                cancellationToken);
        }
        statusDetail = result.Status switch
        {
            PiAgentWorkspaceEditStatus.Applied =>
                reviewedIterationSnapshot?.Status ==
                    PiAgentReviewedIterationStatus.AwaitingTrustedValidation
                    ? $"Applied once: {result.PathLabel}. The repository gate passed; pinned tests now require a separate owner approval."
                    : $"Applied once: {result.PathLabel}. The exact before-hash capability is consumed.",
            PiAgentWorkspaceEditStatus.Drifted =>
                $"Edit not applied: {result.PathLabel} changed after proposal. Review a fresh proposal.",
            _ =>
                $"Edit approval failed closed: {result.ErrorCode ?? "unknown error"}. Restart the session before more work.",
        };
        RaisePropertyChanged(nameof(StatusDetail));
        RaiseConversationProperties();
        RaiseReviewedIterationProperties();
    }

    public async Task RejectWorkspaceEditAsync(
        string proposalId,
        CancellationToken cancellationToken = default)
    {
        uiError = null;
        if (binding is null || !CanReviewWorkspaceEdits)
        {
            throw new InvalidOperationException(
                "The workspace proposal is not ready for rejection.");
        }
        PiAgentWorkspaceEditSnapshot result;
        if (
            reviewedIteration is not null &&
            reviewedIterationSnapshot?.CurrentProposalId == proposalId)
        {
            PiAgentReviewedIterationDecisionResult decision =
                await reviewedIteration.RejectAsync(
                    proposalId,
                    cancellationToken);
            result = decision.Edit;
        }
        else
        {
            result = await binding.RejectWorkspaceEditAsync(
                proposalId,
                cancellationToken);
        }
        statusDetail = result.Status switch
        {
            PiAgentWorkspaceEditStatus.Rejected =>
                $"Rejected without writing: {result.PathLabel}. The proposal capability is consumed.",
            _ =>
                $"Edit rejection failed closed: {result.ErrorCode ?? "unknown error"}. Restart the session before more work.",
        };
        RaisePropertyChanged(nameof(StatusDetail));
        RaiseConversationProperties();
        RaiseReviewedIterationProperties();
    }

    public void ReportUiError(string message)
    {
        uiError = message;
        RaisePropertyChanged(nameof(StatusDetail));
    }

    public async Task ShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        if (runtime is null)
        {
            return;
        }

        SetPhase(
            ConversationRuntimePhase.Stopping,
            "Quiescing submissions, cancelling any active turn and " +
            "suspending reviewed policy receipts before the encrypted checkpoint flush.");
        try
        {
            if (reviewedIteration is not null)
            {
                try
                {
                    await reviewedIteration.SuspendAsync(cancellationToken);
                }
                catch (Exception exception)
                {
                    statusDetail =
                        "Reviewed policy receipt suspension failed closed: " +
                        exception.Message +
                        ". Continuing owned runtime shutdown.";
                    RaisePropertyChanged(nameof(StatusDetail));
                }
            }
            await runtime.ShutdownAsync(cancellationToken);
            SetPhase(
                ConversationRuntimePhase.Stopped,
                "The owned Pi sidecar and broker completed orderly shutdown.");
        }
        catch (Exception exception)
        {
            SetPhase(
                ConversationRuntimePhase.Faulted,
                $"Orderly shutdown reported: {exception.Message}");
        }
        finally
        {
            if (binding is not null)
            {
                binding.PropertyChanged -= OnBindingPropertyChanged;
                binding.Dispose();
                binding = null;
            }
            if (reviewedIteration is not null)
            {
                reviewedIteration.SnapshotChanged -=
                    OnReviewedIterationSnapshotChanged;
                reviewedIteration = null;
            }
            await runtime.DisposeAsync();
            runtime = null;
            RaiseConversationProperties();
            RaiseReviewedIterationProperties();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }
        await ShutdownAsync();
    }

    private async Task ObserveCompletionAsync(PiAgentConversationTurn turn)
    {
        try
        {
            PiAgentConversationTurnSnapshot terminal = await turn.Completion;
            statusDetail = terminal.Status switch
            {
                PiAgentConversationTurnStatus.Completed =>
                    terminal.WorkspaceEdits.Any(edit =>
                            edit.Status ==
                                PiAgentWorkspaceEditStatus.Pending)
                    ? "Turn complete. One workspace proposal is waiting for your explicit owner decision."
                        : "Turn completed and queued for encrypted checkpointing.",
                PiAgentConversationTurnStatus.Aborted =>
                    "Turn aborted; no mutation capability was available.",
                _ =>
                    $"Turn ended as {terminal.Status}: " +
                    $"{terminal.ErrorCode ?? "no error code"}.",
            };
        }
        catch (Exception exception)
        {
            statusDetail = $"Turn completion failed closed: {exception.Message}";
        }
        RaisePropertyChanged(nameof(StatusDetail));
        RaiseRuntimeProperties();
    }

    private async Task ObserveReviewedIterationCompletionAsync(
        PiAgentConversationTurn turn)
    {
        try
        {
            PiAgentConversationTurnSnapshot terminal = await turn.Completion;
            if (reviewedIteration is not null)
            {
                await reviewedIteration.ObserveTurnCompletionAsync(terminal);
            }
            statusDetail = terminal.Status switch
            {
                PiAgentConversationTurnStatus.Completed when
                    terminal.WorkspaceEdits.Any(edit =>
                        edit.Status == PiAgentWorkspaceEditStatus.Pending) =>
                    "Reviewed turn complete. The loop is paused at the owner's one-shot workspace write decision.",
                PiAgentConversationTurnStatus.Completed =>
                    "Reviewed turn completed without a mutation proposal; the loop ended without writing.",
                PiAgentConversationTurnStatus.Aborted =>
                    "Reviewed turn stopped; no continuation is admitted.",
                _ =>
                    $"Reviewed turn failed closed: {terminal.ErrorCode ?? "no error code"}.",
            };
        }
        catch (Exception exception)
        {
            statusDetail =
                $"Reviewed iteration completion failed closed: {exception.Message}";
        }
        RaisePropertyChanged(nameof(StatusDetail));
        RaiseConversationProperties();
        RaiseRuntimeProperties();
        RaiseReviewedIterationProperties();
    }

    private void OnBindingPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (binding is null)
        {
            return;
        }
        snapshot = binding.Snapshot;
        RaiseConversationProperties();
        RaiseRuntimeProperties();
    }

    private void OnReviewedIterationSnapshotChanged(
        object? sender,
        PiAgentReviewedIterationSnapshotChangedEventArgs eventArgs)
    {
        reviewedIterationSnapshot = eventArgs.Snapshot;
        RaiseReviewedIterationProperties();
        RaiseConversationProperties();
    }

    private void SetPhase(
        ConversationRuntimePhase next,
        string detail)
    {
        phase = next;
        statusDetail = detail;
        uiError = null;
        RaiseRuntimeProperties();
        RaiseConversationProperties();
    }

    private double DetermineHandoffProgress()
    {
        PiAgentConversationTurnSnapshot? active = ActiveTurn;
        if (active is null)
        {
            return snapshot.Turns.Count == 0 ? 0 : 3;
        }
        if (active.Tools.Any(tool =>
                tool.Status == PiAgentConversationToolStatus.Running))
        {
            return 2;
        }
        if (active.AssistantText.Length != 0)
        {
            return 3;
        }
        if (active.Tools.Count != 0)
        {
            return 2.5;
        }
        return active.Status == PiAgentConversationTurnStatus.Starting
            ? 0.6
            : 1;
    }

    private void RaiseConversationProperties()
    {
        RaisePropertyChanged(nameof(Turns));
        RaisePropertyChanged(nameof(ActiveTools));
        RaisePropertyChanged(nameof(PendingWorkspaceEdit));
        RaisePropertyChanged(nameof(HasTurns));
        RaisePropertyChanged(nameof(HasActiveTurn));
        RaisePropertyChanged(nameof(HandoffComplete));
        RaisePropertyChanged(nameof(CanSubmit));
        RaisePropertyChanged(nameof(CanCancel));
        RaisePropertyChanged(nameof(CanReviewWorkspaceEdits));
        RaisePropertyChanged(nameof(CanLaunchSession));
        RaisePropertyChanged(nameof(HandoffProgress));
        RaisePropertyChanged(nameof(HandoffLabel));
        RaisePropertyChanged(nameof(EmptyStateTitle));
        RaisePropertyChanged(nameof(EmptyStateDescription));
        RaisePropertyChanged(nameof(SessionLaunchActionLabel));
        RaisePropertyChanged(nameof(CanStartReviewedIteration));
        RaisePropertyChanged(nameof(CanResumeReviewedIteration));
        RaisePropertyChanged(nameof(CanRunTrustedValidation));
        RaisePropertyChanged(nameof(CanStopReviewedIteration));
    }

    private void RaiseReviewedIterationProperties()
    {
        RaisePropertyChanged(nameof(ReviewedIteration));
        RaisePropertyChanged(nameof(HasReviewedIteration));
        RaisePropertyChanged(nameof(CanStartReviewedIteration));
        RaisePropertyChanged(nameof(CanResumeReviewedIteration));
        RaisePropertyChanged(nameof(CanRunTrustedValidation));
        RaisePropertyChanged(nameof(CanStopReviewedIteration));
        RaisePropertyChanged(nameof(ReviewedIterationStatusLabel));
        RaisePropertyChanged(nameof(ReviewedIterationDetail));
        RaisePropertyChanged(nameof(ReviewedIterationProgressLabel));
        RaisePropertyChanged(nameof(ReviewedIterationReceiptLabel));
        RaisePropertyChanged(nameof(ReviewedIterationHeadLabel));
        RaisePropertyChanged(nameof(ReviewedIterationExpiryLabel));
        RaisePropertyChanged(nameof(ReviewedIterationValidationProfileLabel));
        RaisePropertyChanged(nameof(ReviewedIterationValidationCommand));
    }

    private void RaiseRuntimeProperties()
    {
        RaisePropertyChanged(nameof(Phase));
        RaisePropertyChanged(nameof(PhaseLabel));
        RaisePropertyChanged(nameof(StatusDetail));
        RaisePropertyChanged(nameof(ProviderLabel));
        RaisePropertyChanged(nameof(WorkspaceLabel));
        RaisePropertyChanged(nameof(CheckpointLabel));
        RaisePropertyChanged(nameof(CredentialLabel));
        RaisePropertyChanged(nameof(BrokerLabel));
        RaisePropertyChanged(nameof(ShutdownLabel));
        RaisePropertyChanged(nameof(HasOwnedRuntime));
        RaisePropertyChanged(nameof(CanLaunchSession));
        RaisePropertyChanged(nameof(EmptyStateTitle));
        RaisePropertyChanged(nameof(EmptyStateDescription));
        RaisePropertyChanged(nameof(SessionLaunchActionLabel));
    }

    private static PiAgentConversationSnapshot CreatePreviewSnapshot()
    {
        PiAgentConversationTurnSnapshot completed = new(
            "preview-turn-1",
            UiText.Get("Loc.Runtime.Preview.User"),
            UiText.Get("Loc.Runtime.Preview.Assistant"),
            PiAgentConversationTurnStatus.Completed,
            4,
            false,
            [
                new PiAgentConversationToolSnapshot(
                    "preview-tool-1",
                    "ls // illustrative",
                    PiAgentConversationToolStatus.Completed,
                    1,
                    3),
            ],
            [
                new PiAgentWorkspaceEditSnapshot(
                    4,
                    "workspace-edit-0123456789abcdef0123456789abcdef",
                    "change-set",
                    "",
                    "38c7f12af806e7c5bb13bb7d557cc4210dd31e2d75461b208f07600da9d7f214",
                    "",
                    "",
                    [],
                    PiAgentWorkspaceEditStatus.Pending,
                    null,
                    null)
                {
                    FileChanges =
                    [
                        new PiAgentWorkspaceFileReviewSnapshot(
                            1,
                            "replace",
                            "src/common/Jarvis.PiAgentHost/ReviewedIterationCoordinator.cs",
                            "84c7f12af806e7c5bb13bb7d557cc4210dd31e2d75461b208f07600da9d7f211",
                            "MaximumApprovedEdits = 3",
                            "MaximumApprovedEdits = 4",
                            [],
                            null),
                        new PiAgentWorkspaceFileReviewSnapshot(
                            2,
                            "patch",
                            "src/common/Jarvis.PiAgentHost/test/reviewed-iteration.test.mjs",
                            "74c7f12af806e7c5bb13bb7d557cc4210dd31e2d75461b208f07600da9d7f212",
                            "",
                            "",
                            [
                                new PiAgentWorkspacePatchHunk(
                                    1,
                                    "assert.equal(files.length, 1);",
                                    "assert.equal(files.length, 3);"),
                                new PiAgentWorkspacePatchHunk(
                                    2,
                                    "assert.equal(recovery, false);",
                                    "assert.equal(recovery, true);"),
                            ],
                            null),
                        new PiAgentWorkspaceFileReviewSnapshot(
                            3,
                            "create",
                            "docs/PI-AGENT-MULTI-FILE-TRANSACTION.md",
                            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                            "",
                            "# Durable change set\n\nOwner review covers all files once.\n",
                            [],
                            null),
                    ],
                },
            ],
            null);
        return new PiAgentConversationSnapshot(
            1,
            null,
            false,
            false,
            [completed]);
    }

    private void RaisePropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
