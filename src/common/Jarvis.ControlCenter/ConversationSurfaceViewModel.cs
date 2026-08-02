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
            ? "Illustrative conversation data; no runtime was started."
            : "Launch with an admitted workspace and the packaged Pi runtime.";
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
        ConversationRuntimePhase.NotStarted => "NOT STARTED",
        ConversationRuntimePhase.Preview => "DESIGN PREVIEW",
        ConversationRuntimePhase.Starting => "STARTING",
        ConversationRuntimePhase.Ready => "READY",
        ConversationRuntimePhase.Stopping => "STOPPING",
        ConversationRuntimePhase.Stopped => "STOPPED",
        ConversationRuntimePhase.Faulted => "FAULTED",
        _ => "UNKNOWN",
    };
    public string StatusDetail => uiError ?? statusDetail;
    public string ProviderLabel => preview
        ? "ILLUSTRATIVE // NO RUNTIME"
        : launchOptions?.ProviderDisplayName ?? "NO PROVIDER ADMITTED";
    public string AccessLabel => "READ + REVIEWED EDIT";
    public string WorkspaceLabel => launchOptions?.WorkspaceRoot ??
        (preview
            ? "ILLUSTRATIVE // NOT ADMITTED"
            : "NO WORKSPACE ADMITTED");
    public string CheckpointLabel => runtime is null
        ? preview ? "ILLUSTRATIVE // NOT SAVED" : "NOT LOADED"
        : runtime.CheckpointPersistenceFaulted
            ? "FAULTED / SUBMISSIONS CLOSED"
            : $"{runtime.CheckpointSaveCount} SAVED / " +
                $"{runtime.RestoredCheckpointTurnCount} RESTORED";
    public string CredentialLabel => runtime?.CredentialEnvironmentClean == true
        ? launchOptions?.Provider == ConversationProviderKind.OpenAiResponses
            ? providerCredentialReady
                ? "DESKTOP DPAPI READY // SIDECAR CLEAN"
                : "OPENAI AUTH REQUIRED // SIDECAR CLEAN"
            : providerCredentialReady
                ? "OPENAI STORED // LOCAL PROVIDER ACTIVE"
                : "SIDECAR CLEAN // OPENAI OPTIONAL"
        : preview
            ? "NOT CONFIGURED / NOT EVALUATED"
            : "NOT READY";
    public string BrokerLabel => runtime is null
        ? preview ? "ILLUSTRATIVE // NOT STARTED" : "NO BROKER"
        : $"{runtime.BrokerRequestCount} REQUESTS / " +
            $"{runtime.BrokerFaultCount} FAULTS";
    public string ShutdownLabel => phase switch
    {
        ConversationRuntimePhase.Stopping => "QUIESCING ACTIVE TURN",
        ConversationRuntimePhase.Stopped => "OWNED RUNTIME RELEASED",
        ConversationRuntimePhase.Ready => "ORDERLY SHUTDOWN ARMED",
        _ => preview
            ? "ILLUSTRATIVE // NO OWNED RUNTIME"
            : "NO OWNED RUNTIME",
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
    public bool HandoffComplete =>
        snapshot.ActiveTurnId is null && snapshot.Turns.Count != 0;
    public bool CanSubmit =>
        phase == ConversationRuntimePhase.Ready &&
        snapshot.CanSubmit &&
        (launchOptions?.Provider != ConversationProviderKind.OpenAiResponses ||
            providerCredentialReady);
    public bool CanCancel =>
        phase == ConversationRuntimePhase.Ready && snapshot.CanCancel;
    public bool CanReviewWorkspaceEdits =>
        phase == ConversationRuntimePhase.Ready &&
        snapshot.ActiveTurnId is null &&
        PendingWorkspaceEdit?.CanDecide == true;
    public bool CanLaunchSession =>
        !preview &&
        runtime is null &&
        phase is ConversationRuntimePhase.NotStarted or
            ConversationRuntimePhase.Faulted;
    public bool HasOwnedRuntime => runtime is not null && !runtime.IsShutdown;
    public bool IsOpenAiProvider =>
        launchOptions?.Provider == ConversationProviderKind.OpenAiResponses;
    public double HandoffProgress => DetermineHandoffProgress();
    public string HandoffLabel => HandoffProgress switch
    {
        _ when PendingWorkspaceEdit is not null =>
            "OWNER HOLDS A ONE-SHOT EDIT DECISION",
        <= 0 => "USER HOLDS THE NEXT TURN",
        < 2 => "PI RUNTIME OWNS THE ACTIVE TURN",
        < 3 => "READ TOOL OWNS THE ACTIVE TURN",
        _ => snapshot.ActiveTurnId is null
            ? "TURN COMPLETE / CONTROL RETURNED"
            : "JARVIS IS STREAMING A RESPONSE",
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
            "Submit a request below. Reads are root-confined; edits can only be staged for your explicit review.",
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
            SetPhase(
                ConversationRuntimePhase.Ready,
                IsOpenAiProvider && !providerCredentialReady
                    ? "Pi is ready. Configure OpenAI to enable authenticated turns."
                    : "Pi session admitted. Reads are root-confined and " +
                        "edit proposals require a one-shot owner decision.");
            RaiseConversationProperties();
        }
        catch (Exception exception)
        {
            SetPhase(
                ConversationRuntimePhase.Faulted,
                $"Runtime admission failed: {exception.Message}");
            if (runtime is not null)
            {
                await runtime.DisposeAsync();
                runtime = null;
            }
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

    public async Task ApplyWorkspaceEditAsync(
        string proposalId,
        CancellationToken cancellationToken = default)
    {
        uiError = null;
        if (binding is null || !CanReviewWorkspaceEdits)
        {
            throw new InvalidOperationException(
                "The workspace edit proposal is not ready for approval.");
        }
        PiAgentWorkspaceEditSnapshot result =
            await binding.ApplyWorkspaceEditAsync(
                proposalId,
                cancellationToken);
        statusDetail = result.Status switch
        {
            PiAgentWorkspaceEditStatus.Applied =>
                $"Applied once: {result.RelativePath}. The exact before-hash capability is consumed.",
            PiAgentWorkspaceEditStatus.Drifted =>
                $"Edit not applied: {result.RelativePath} changed after proposal. Review a fresh proposal.",
            _ =>
                $"Edit approval failed closed: {result.ErrorCode ?? "unknown error"}. Restart the session before more work.",
        };
        RaisePropertyChanged(nameof(StatusDetail));
        RaiseConversationProperties();
    }

    public async Task RejectWorkspaceEditAsync(
        string proposalId,
        CancellationToken cancellationToken = default)
    {
        uiError = null;
        if (binding is null || !CanReviewWorkspaceEdits)
        {
            throw new InvalidOperationException(
                "The workspace edit proposal is not ready for rejection.");
        }
        PiAgentWorkspaceEditSnapshot result =
            await binding.RejectWorkspaceEditAsync(
                proposalId,
                cancellationToken);
        statusDetail = result.Status switch
        {
            PiAgentWorkspaceEditStatus.Rejected =>
                $"Rejected without writing: {result.RelativePath}. The proposal capability is consumed.",
            _ =>
                $"Edit rejection failed closed: {result.ErrorCode ?? "unknown error"}. Restart the session before more work.",
        };
        RaisePropertyChanged(nameof(StatusDetail));
        RaiseConversationProperties();
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
            "flushing the encrypted checkpoint.");
        try
        {
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
            await runtime.DisposeAsync();
            runtime = null;
            RaiseConversationProperties();
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
                        ? "Turn complete. One edit proposal is waiting for your explicit owner decision."
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
            "[ILLUSTRATIVE] Inspect the workspace boundary.",
            "Illustrative handoff complete. No workspace, broker, sidecar, " +
                "or Pi tool was started in preview mode.",
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
                    1,
                    "workspace-edit-0123456789abcdef0123456789abcdef",
                    "src/common/Jarvis.ControlCenter/ConversationSurfaceViewModel.cs",
                    new string('a', 64),
                    "public string AccessLabel => \"READ ONLY\";",
                    "public string AccessLabel => \"READ + REVIEWED EDIT\";",
                    PiAgentWorkspaceEditStatus.Pending,
                    null,
                    null),
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
