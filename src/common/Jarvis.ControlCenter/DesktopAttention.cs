using Jarvis.PiAgentHost;

namespace Jarvis.ControlCenter;

internal enum DesktopAttentionKind
{
    Ready,
    Working,
    Completed,
    OwnerActionRequired,
    Faulted,
}

internal sealed record DesktopAttentionSnapshot(
    DesktopAttentionKind Kind,
    string SignalKey,
    string? TargetTurnId = null,
    string? TargetProposalId = null);

internal sealed record DesktopAttentionInput
{
    public ConversationRuntimePhase RuntimePhase { get; init; }
    public string? ActiveTurnId { get; init; }
    public string? LatestTurnId { get; init; }
    public PiAgentConversationTurnStatus? LatestTurnStatus { get; init; }
    public string? PendingProposalId { get; init; }
    public string? PendingEditTurnId { get; init; }
    public PiAgentWorkspaceEditStatus? PendingEditStatus { get; init; }
    public string? LatestProposalId { get; init; }
    public string? LatestEditTurnId { get; init; }
    public PiAgentWorkspaceEditStatus? LatestEditStatus { get; init; }
    public string? IterationId { get; init; }
    public string? IterationTurnId { get; init; }
    public PiAgentReviewedIterationStatus? IterationStatus { get; init; }
}

internal static class DesktopAttentionModel
{
    public static DesktopAttentionSnapshot Select(
        DesktopAttentionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.RuntimePhase == ConversationRuntimePhase.Faulted)
        {
            return new(
                DesktopAttentionKind.Faulted,
                "runtime:faulted",
                input.LatestTurnId);
        }
        bool iterationOwnsActiveTurn =
            input.ActiveTurnId is not null &&
            string.Equals(
                input.ActiveTurnId,
                input.IterationTurnId,
                StringComparison.Ordinal);
        bool iterationOwnsLatestTurn =
            input.LatestTurnId is not null &&
            string.Equals(
                input.LatestTurnId,
                input.IterationTurnId,
                StringComparison.Ordinal);
        bool iterationRelevant =
            input.IterationStatus is not null &&
            (
                !IsTerminalIteration(input.IterationStatus.Value) ||
                iterationOwnsActiveTurn ||
                iterationOwnsLatestTurn ||
                input.LatestTurnId is null
            );
        if (input.ActiveTurnId is not null)
        {
            bool iterationWorking =
                iterationRelevant &&
                iterationOwnsActiveTurn &&
                IsWorkingIteration(input.IterationStatus);
            return new(
                DesktopAttentionKind.Working,
                iterationWorking
                    ? CreateKey("iteration", input.IterationId)
                    : CreateKey("turn", input.ActiveTurnId),
                input.ActiveTurnId);
        }
        if (input.LatestEditStatus is
            PiAgentWorkspaceEditStatus.Drifted or
            PiAgentWorkspaceEditStatus.Failed)
        {
            return new(
                DesktopAttentionKind.Faulted,
                CreateKey("proposal", input.LatestProposalId),
                input.LatestEditTurnId,
                input.LatestProposalId);
        }
        if (
            iterationRelevant &&
            input.IterationStatus is
            PiAgentReviewedIterationStatus.Expired or
            PiAgentReviewedIterationStatus.Faulted)
        {
            return new(
                DesktopAttentionKind.Faulted,
                CreateKey("iteration", input.IterationId),
                input.IterationTurnId);
        }
        if (input.LatestTurnStatus == PiAgentConversationTurnStatus.Failed)
        {
            return new(
                DesktopAttentionKind.Faulted,
                CreateKey("turn", input.LatestTurnId),
                input.LatestTurnId);
        }
        if (
            input.PendingEditStatus == PiAgentWorkspaceEditStatus.Pending ||
            (
                iterationRelevant &&
                input.IterationStatus is
                    PiAgentReviewedIterationStatus.AwaitingOwnerReview or
                    PiAgentReviewedIterationStatus.AwaitingTrustedValidation or
                    PiAgentReviewedIterationStatus.Interrupted
            ))
        {
            string category = input.PendingEditStatus ==
                PiAgentWorkspaceEditStatus.Pending
                    ? "proposal"
                    : "iteration";
            string? identity = category == "proposal"
                ? input.PendingProposalId
                : input.IterationId;
            string? targetTurnId = category == "proposal"
                ? input.PendingEditTurnId
                : input.IterationTurnId;
            return new(
                DesktopAttentionKind.OwnerActionRequired,
                CreateKey(category, identity),
                targetTurnId,
                category == "proposal"
                    ? input.PendingProposalId
                    : null);
        }
        if (
            input.LatestEditStatus is
                PiAgentWorkspaceEditStatus.Applying or
                PiAgentWorkspaceEditStatus.Rejecting)
        {
            return new(
                DesktopAttentionKind.Working,
                CreateKey("proposal", input.LatestProposalId),
                input.LatestEditTurnId,
                input.LatestProposalId);
        }
        if (
            iterationRelevant &&
            input.IterationStatus == PiAgentReviewedIterationStatus.Completed)
        {
            return new(
                DesktopAttentionKind.Completed,
                CreateKey("iteration", input.IterationId),
                input.IterationTurnId);
        }
        if (
            input.RuntimePhase is
                ConversationRuntimePhase.Starting or
                ConversationRuntimePhase.Stopping ||
            (
                iterationRelevant &&
                IsWorkingIteration(input.IterationStatus)
            ))
        {
            return new(
                DesktopAttentionKind.Working,
                input.IterationId is null
                    ? $"runtime:{input.RuntimePhase}"
                    : CreateKey("iteration", input.IterationId),
                input.IterationTurnId);
        }
        if (input.LatestTurnStatus == PiAgentConversationTurnStatus.Completed)
        {
            return new(
                DesktopAttentionKind.Completed,
                CreateKey("turn", input.LatestTurnId),
                input.LatestTurnId);
        }
        return new(
            DesktopAttentionKind.Ready,
            $"runtime:{input.RuntimePhase}");
    }

    private static bool IsTerminalIteration(
        PiAgentReviewedIterationStatus status) =>
        status is
            PiAgentReviewedIterationStatus.Completed or
            PiAgentReviewedIterationStatus.Stopped or
            PiAgentReviewedIterationStatus.Expired or
            PiAgentReviewedIterationStatus.Faulted;

    private static bool IsWorkingIteration(
        PiAgentReviewedIterationStatus? status) =>
        status is
            PiAgentReviewedIterationStatus.ActiveTurn or
            PiAgentReviewedIterationStatus.DecisionInFlight or
            PiAgentReviewedIterationStatus.Validating or
            PiAgentReviewedIterationStatus.TrustedValidationInFlight or
            PiAgentReviewedIterationStatus.ReadyToContinue;

    public static bool ShouldSignal(
        DesktopAttentionSnapshot previous,
        DesktopAttentionSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (previous == current)
        {
            return false;
        }
        if (current.Kind is
            DesktopAttentionKind.OwnerActionRequired or
            DesktopAttentionKind.Faulted)
        {
            return true;
        }
        return
            current.Kind == DesktopAttentionKind.Completed &&
            previous.Kind == DesktopAttentionKind.Working &&
            string.Equals(
                previous.SignalKey,
                current.SignalKey,
                StringComparison.Ordinal);
    }

    private static string CreateKey(string category, string? identity) =>
        $"{category}:{identity ?? "unknown"}";
}

public sealed record DesktopAttentionProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    bool ReadyPassed,
    bool ActiveTurnPassed,
    bool TurnCompletionSignalPassed,
    bool ReviewedIterationCompletionSignalPassed,
    bool TerminalIterationOrdinaryTurnPassed,
    bool StartupReplaySuppressed,
    bool OwnerReviewSignalPassed,
    bool TrustedValidationSignalPassed,
    bool DriftedEditSignalPassed,
    bool FailedEditSignalPassed,
    bool FaultSignalPassed,
    bool AttentionTargetsPassed,
    bool DuplicateSignalSuppressed,
    bool ContentFreeSignalKeysPassed,
    IReadOnlyList<string> Failures);

public static class DesktopAttentionProbe
{
    public static DesktopAttentionProbeReceipt Run()
    {
        List<string> failures = [];
        DesktopAttentionSnapshot ready = DesktopAttentionModel.Select(
            new DesktopAttentionInput
            {
                RuntimePhase = ConversationRuntimePhase.Ready,
            });
        DesktopAttentionSnapshot active = DesktopAttentionModel.Select(
            new DesktopAttentionInput
            {
                RuntimePhase = ConversationRuntimePhase.Ready,
                ActiveTurnId = "turn-a",
                LatestTurnId = "turn-a",
                LatestTurnStatus = PiAgentConversationTurnStatus.Running,
            });
        DesktopAttentionSnapshot completed = DesktopAttentionModel.Select(
            new DesktopAttentionInput
            {
                RuntimePhase = ConversationRuntimePhase.Ready,
                LatestTurnId = "turn-a",
                LatestTurnStatus = PiAgentConversationTurnStatus.Completed,
            });
        DesktopAttentionSnapshot startup = DesktopAttentionModel.Select(
            new DesktopAttentionInput
            {
                RuntimePhase = ConversationRuntimePhase.Starting,
            });
        DesktopAttentionSnapshot owner = DesktopAttentionModel.Select(
            new DesktopAttentionInput
            {
                RuntimePhase = ConversationRuntimePhase.Ready,
                PendingProposalId = "proposal-a",
                PendingEditTurnId = "turn-proposal",
                PendingEditStatus = PiAgentWorkspaceEditStatus.Pending,
            });
        DesktopAttentionSnapshot trustedValidation =
            DesktopAttentionModel.Select(
                new DesktopAttentionInput
                {
                    RuntimePhase = ConversationRuntimePhase.Ready,
                    IterationId = "iteration-a",
                    IterationTurnId = "turn-reviewed",
                    IterationStatus = PiAgentReviewedIterationStatus
                        .AwaitingTrustedValidation,
                });
        DesktopAttentionSnapshot fault = DesktopAttentionModel.Select(
            new DesktopAttentionInput
            {
                RuntimePhase = ConversationRuntimePhase.Ready,
                LatestTurnId = "turn-b",
                LatestTurnStatus = PiAgentConversationTurnStatus.Failed,
            });
        DesktopAttentionSnapshot reviewedActive =
            DesktopAttentionModel.Select(
                new DesktopAttentionInput
                {
                    RuntimePhase = ConversationRuntimePhase.Ready,
                    ActiveTurnId = "turn-reviewed",
                    LatestTurnId = "turn-reviewed",
                    LatestTurnStatus = PiAgentConversationTurnStatus.Running,
                    IterationId = "iteration-reviewed",
                    IterationTurnId = "turn-reviewed",
                    IterationStatus =
                        PiAgentReviewedIterationStatus.ActiveTurn,
                });
        DesktopAttentionSnapshot reviewedCompleted =
            DesktopAttentionModel.Select(
                new DesktopAttentionInput
                {
                    RuntimePhase = ConversationRuntimePhase.Ready,
                    LatestTurnId = "turn-reviewed",
                    LatestTurnStatus =
                        PiAgentConversationTurnStatus.Completed,
                    IterationId = "iteration-reviewed",
                    IterationTurnId = "turn-reviewed",
                    IterationStatus =
                        PiAgentReviewedIterationStatus.Completed,
                });
        DesktopAttentionSnapshot ordinaryAfterTerminalActive =
            DesktopAttentionModel.Select(
                new DesktopAttentionInput
                {
                    RuntimePhase = ConversationRuntimePhase.Ready,
                    ActiveTurnId = "turn-after-iteration",
                    LatestTurnId = "turn-after-iteration",
                    LatestTurnStatus = PiAgentConversationTurnStatus.Running,
                    IterationId = "iteration-reviewed",
                    IterationTurnId = "turn-reviewed",
                    IterationStatus =
                        PiAgentReviewedIterationStatus.Completed,
                });
        DesktopAttentionSnapshot ordinaryAfterTerminalCompleted =
            DesktopAttentionModel.Select(
                new DesktopAttentionInput
                {
                    RuntimePhase = ConversationRuntimePhase.Ready,
                    LatestTurnId = "turn-after-iteration",
                    LatestTurnStatus =
                        PiAgentConversationTurnStatus.Completed,
                    IterationId = "iteration-reviewed",
                    IterationTurnId = "turn-reviewed",
                    IterationStatus =
                        PiAgentReviewedIterationStatus.Completed,
                });
        DesktopAttentionSnapshot pendingEdit =
            DesktopAttentionModel.Select(
                new DesktopAttentionInput
                {
                    RuntimePhase = ConversationRuntimePhase.Ready,
                    PendingProposalId = "proposal-edit",
                    PendingEditTurnId = "turn-edit",
                    PendingEditStatus = PiAgentWorkspaceEditStatus.Pending,
                    LatestProposalId = "proposal-edit",
                    LatestEditTurnId = "turn-edit",
                    LatestEditStatus = PiAgentWorkspaceEditStatus.Pending,
                });
        DesktopAttentionSnapshot driftedEdit =
            DesktopAttentionModel.Select(
                new DesktopAttentionInput
                {
                    RuntimePhase = ConversationRuntimePhase.Ready,
                    LatestProposalId = "proposal-edit",
                    LatestEditTurnId = "turn-edit",
                    LatestEditStatus = PiAgentWorkspaceEditStatus.Drifted,
                });
        DesktopAttentionSnapshot failedEdit =
            DesktopAttentionModel.Select(
                new DesktopAttentionInput
                {
                    RuntimePhase = ConversationRuntimePhase.Ready,
                    LatestProposalId = "proposal-edit",
                    LatestEditTurnId = "turn-edit",
                    LatestEditStatus = PiAgentWorkspaceEditStatus.Failed,
                });

        bool readyPassed = ready.Kind == DesktopAttentionKind.Ready;
        bool activeTurnPassed =
            active.Kind == DesktopAttentionKind.Working &&
            active.SignalKey == "turn:turn-a";
        bool turnCompletionSignalPassed =
            completed.Kind == DesktopAttentionKind.Completed &&
            DesktopAttentionModel.ShouldSignal(active, completed);
        bool reviewedIterationCompletionSignalPassed =
            reviewedActive.Kind == DesktopAttentionKind.Working &&
            reviewedActive.SignalKey == "iteration:iteration-reviewed" &&
            reviewedCompleted.Kind == DesktopAttentionKind.Completed &&
            DesktopAttentionModel.ShouldSignal(
                reviewedActive,
                reviewedCompleted);
        bool terminalIterationOrdinaryTurnPassed =
            ordinaryAfterTerminalActive.Kind ==
                DesktopAttentionKind.Working &&
            ordinaryAfterTerminalActive.SignalKey ==
                "turn:turn-after-iteration" &&
            ordinaryAfterTerminalCompleted.Kind ==
                DesktopAttentionKind.Completed &&
            DesktopAttentionModel.ShouldSignal(
                ordinaryAfterTerminalActive,
                ordinaryAfterTerminalCompleted);
        bool startupReplaySuppressed =
            !DesktopAttentionModel.ShouldSignal(startup, completed);
        bool ownerReviewSignalPassed =
            owner.Kind == DesktopAttentionKind.OwnerActionRequired &&
            DesktopAttentionModel.ShouldSignal(active, owner);
        bool trustedValidationSignalPassed =
            trustedValidation.Kind ==
                DesktopAttentionKind.OwnerActionRequired &&
            DesktopAttentionModel.ShouldSignal(active, trustedValidation);
        bool faultSignalPassed =
            fault.Kind == DesktopAttentionKind.Faulted &&
            DesktopAttentionModel.ShouldSignal(active, fault);
        bool attentionTargetsPassed =
            active.TargetTurnId == "turn-a" &&
            completed.TargetTurnId == "turn-a" &&
            completed.TargetProposalId is null &&
            owner.TargetTurnId == "turn-proposal" &&
            owner.TargetProposalId == "proposal-a" &&
            trustedValidation.TargetTurnId == "turn-reviewed" &&
            trustedValidation.TargetProposalId is null &&
            reviewedCompleted.TargetTurnId == "turn-reviewed" &&
            driftedEdit.TargetTurnId == "turn-edit" &&
            driftedEdit.TargetProposalId == "proposal-edit" &&
            failedEdit.TargetTurnId == "turn-edit" &&
            failedEdit.TargetProposalId == "proposal-edit";
        bool driftedEditSignalPassed =
            driftedEdit.Kind == DesktopAttentionKind.Faulted &&
            DesktopAttentionModel.ShouldSignal(pendingEdit, driftedEdit);
        bool failedEditSignalPassed =
            failedEdit.Kind == DesktopAttentionKind.Faulted &&
            DesktopAttentionModel.ShouldSignal(pendingEdit, failedEdit);
        bool duplicateSignalSuppressed =
            !DesktopAttentionModel.ShouldSignal(owner, owner) &&
            !DesktopAttentionModel.ShouldSignal(fault, fault);
        string signalKeys = string.Join(
            "|",
            ready.SignalKey,
            active.SignalKey,
            completed.SignalKey,
            owner.SignalKey,
            trustedValidation.SignalKey,
            fault.SignalKey,
            reviewedActive.SignalKey,
            ordinaryAfterTerminalActive.SignalKey,
            driftedEdit.SignalKey,
            failedEdit.SignalKey);
        bool contentFreeSignalKeysPassed =
            !signalKeys.Contains("prompt", StringComparison.OrdinalIgnoreCase) &&
            !signalKeys.Contains("workspace", StringComparison.OrdinalIgnoreCase) &&
            !signalKeys.Contains('\\');

        AddFailure(failures, readyPassed, "ready-selection-failed");
        AddFailure(failures, activeTurnPassed, "active-turn-selection-failed");
        AddFailure(
            failures,
            turnCompletionSignalPassed,
            "turn-completion-signal-failed");
        AddFailure(
            failures,
            reviewedIterationCompletionSignalPassed,
            "reviewed-iteration-completion-signal-failed");
        AddFailure(
            failures,
            terminalIterationOrdinaryTurnPassed,
            "terminal-iteration-masked-ordinary-turn");
        AddFailure(
            failures,
            startupReplaySuppressed,
            "startup-replay-was-not-suppressed");
        AddFailure(
            failures,
            ownerReviewSignalPassed,
            "owner-review-signal-failed");
        AddFailure(
            failures,
            trustedValidationSignalPassed,
            "trusted-validation-signal-failed");
        AddFailure(
            failures,
            driftedEditSignalPassed,
            "drifted-edit-signal-failed");
        AddFailure(
            failures,
            failedEditSignalPassed,
            "failed-edit-signal-failed");
        AddFailure(failures, faultSignalPassed, "fault-signal-failed");
        AddFailure(
            failures,
            attentionTargetsPassed,
            "attention-target-selection-failed");
        AddFailure(
            failures,
            duplicateSignalSuppressed,
            "duplicate-signal-was-not-suppressed");
        AddFailure(
            failures,
            contentFreeSignalKeysPassed,
            "signal-key-contained-user-content");

        return new(
            1,
            "jarvisv2-desktop-attention-probe",
            failures.Count == 0 ? "passed" : "failed",
            readyPassed,
            activeTurnPassed,
            turnCompletionSignalPassed,
            reviewedIterationCompletionSignalPassed,
            terminalIterationOrdinaryTurnPassed,
            startupReplaySuppressed,
            ownerReviewSignalPassed,
            trustedValidationSignalPassed,
            driftedEditSignalPassed,
            failedEditSignalPassed,
            faultSignalPassed,
            attentionTargetsPassed,
            duplicateSignalSuppressed,
            contentFreeSignalKeysPassed,
            failures);
    }

    private static void AddFailure(
        List<string> failures,
        bool passed,
        string failure)
    {
        if (!passed)
        {
            failures.Add(failure);
        }
    }
}
