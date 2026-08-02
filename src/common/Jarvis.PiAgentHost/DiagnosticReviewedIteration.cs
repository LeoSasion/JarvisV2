using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentReviewedIterationProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    bool CleanBaselineRequired,
    bool DurableReceiptRoundTripPassed,
    bool DurableReceiptCiphertextPassed,
    bool OwnerPolicyPassed,
    bool FirstProposalPausedForOwner,
    bool ApprovedEditValidated,
    bool ApprovedNewFileValidated,
    bool ApprovedPatchValidated,
    bool ApprovedChangeSetValidated,
    bool ChangeSetFileReceiptsPersisted,
    bool CaseAliasedFileReceiptsRejected,
    bool ProposalFreeWorkspaceReceiptsRejected,
    bool UntrackedWhitespaceRejected,
    bool SeparateTrustedValidationApprovalRequired,
    bool TrustedValidationPassed,
    bool ModifiedTrustedTestRejected,
    bool TrustedValidationCancellationPassed,
    bool AutomaticReasoningContinuationPassed,
    bool SecondProposalPausedForOwner,
    bool RejectionStoppedLoop,
    bool ShutdownSuspensionPassed,
    bool RestartDidNotRestoreProposalCapability,
    bool ExplicitRearmPassed,
    bool RepositoryDriftRejected,
    bool ShellAvailableToPi,
    bool ValidationProcessAvailableToPi,
    bool UnattendedApprovalAllowed,
    int MaximumApprovedEdits,
    int PolicyLifetimeHours,
    int DurableReceiptFileCount,
    int BrokerRequestCount,
    int BrokerFaultCount,
    string LiveExplorer,
    bool ProductionWorkspaceMutationPerformed);

public static class PiAgentReviewedIterationProbe
{
    public static async Task<PiAgentReviewedIterationProbeReceipt> RunAsync(
        PiAgentSidecarOptions sidecarOptions,
        string gitExecutable,
        CancellationToken cancellationToken)
    {
        string parentRoot = ResolveWorkspaceRoot(sidecarOptions);
        string fixtureRoot = Path.Combine(
            parentRoot,
            $".jarvis-reviewed-iteration-{Guid.NewGuid():N}");
        string storeRoot = Path.Combine(
            parentRoot,
            $".jarvis-reviewed-iteration-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureRoot);
        Directory.CreateDirectory(storeRoot);
        string fixturePath = Path.Combine(fixtureRoot, "review.txt");
        string createdFixturePath = Path.Combine(
            fixtureRoot,
            "generated.txt");
        string validationMarkerPath = Path.Combine(
            Path.GetTempPath(),
            "jarvis-reviewed-validation-" +
                Path.GetFileName(fixtureRoot) +
                ".marker");
        string validationCancelStartedPath = Path.Combine(
            Path.GetTempPath(),
            "jarvis-reviewed-validation-" +
                Path.GetFileName(fixtureRoot) +
                "-cancel-started.marker");
        string validationCancelCompletedPath = Path.Combine(
            Path.GetTempPath(),
            "jarvis-reviewed-validation-" +
                Path.GetFileName(fixtureRoot) +
                "-cancel-completed.marker");
        int brokerRequests = 0;
        int brokerFaults = 0;
        try
        {
            await File.WriteAllTextAsync(
                fixturePath,
                "alpha\nowner-reviewed\nomega\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            Directory.CreateDirectory(Path.Combine(fixtureRoot, "config"));
            Directory.CreateDirectory(Path.Combine(fixtureRoot, "docs"));
            await File.WriteAllTextAsync(
                Path.Combine(
                    fixtureRoot,
                    PiAgentReviewedIterationTrustedValidator
                        .ManifestRelativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)),
                """
                {
                  "schemaVersion": 1,
                  "profileId": "jarvisv2-reviewed-fixture-v1",
                  "runner": "node-test",
                  "timeoutSeconds": 30,
                  "testFiles": ["trusted-validation.test.mjs"]
                }
                """ + "\n",
                new UTF8Encoding(false),
                cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(fixtureRoot, "trusted-validation.test.mjs"),
                """
                import assert from "node:assert/strict";
                import { appendFile, readFile } from "node:fs/promises";
                import { tmpdir } from "node:os";
                import { basename, join } from "node:path";

                const content = await readFile("generated.txt", "utf8");
                assert.match(content, /^(owner-created|OWNER-patched)\n$/);
                await appendFile(
                  join(
                    tmpdir(),
                    `jarvis-reviewed-validation-${basename(process.cwd())}.marker`,
                  ),
                  "run\n",
                  "utf8",
                );
                """ + "\n",
                new UTF8Encoding(false),
                cancellationToken);
            await RunGitAsync(
                gitExecutable,
                fixtureRoot,
                ["init", "--quiet"],
                cancellationToken);
            await RunGitAsync(
                gitExecutable,
                fixtureRoot,
                ["config", "user.name", "JarvisV2 Probe"],
                cancellationToken);
            await RunGitAsync(
                gitExecutable,
                fixtureRoot,
                ["config", "user.email", "jarvis-probe@invalid.local"],
                cancellationToken);
            await RunGitAsync(
                gitExecutable,
                fixtureRoot,
                [
                    "add",
                    "--",
                    "review.txt",
                    "config/pi-agent-trusted-validation.json",
                    "trusted-validation.test.mjs",
                ],
                cancellationToken);
            await RunGitAsync(
                gitExecutable,
                fixtureRoot,
                ["commit", "--quiet", "-m", "probe baseline"],
                cancellationToken);

            PiAgentReviewedIterationStore store = new(storeRoot);
            PiAgentReviewedIterationRepositoryGate gate = new(
                Path.GetFullPath(gitExecutable));
            PiAgentRepositoryBaselineReceipt baseline =
                await gate.CaptureCleanBaselineAsync(
                    fixtureRoot,
                    cancellationToken);
            bool cleanBaselineRequired = baseline.RepositoryRoot ==
                    Path.GetFullPath(fixtureRoot) &&
                baseline.ValidationProfile ==
                    PiAgentReviewedIterationAdmission.ValidationProfile;
            string whitespacePath = Path.Combine(
                fixtureRoot,
                "untracked-whitespace.txt");
            await File.WriteAllTextAsync(
                whitespacePath,
                "trailing whitespace \n",
                new UTF8Encoding(false),
                cancellationToken);
            string whitespaceHash = Convert.ToHexString(
                    SHA256.HashData(
                        await File.ReadAllBytesAsync(
                            whitespacePath,
                            cancellationToken)))
                .ToLowerInvariant();
            PiAgentRepositoryValidationReceipt whitespaceValidation =
                await gate.ValidateAsync(
                    fixtureRoot,
                    baseline.Head,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["untracked-whitespace.txt"] = whitespaceHash,
                    },
                    cancellationToken);
            bool untrackedWhitespaceRejected =
                !whitespaceValidation.Passed;
            File.Delete(whitespacePath);

            DiagnosticIterationModelProvider provider = new();
            bool firstProposalPaused;
            bool approvedValidated;
            bool approvedNewFileValidated;
            bool approvedPatchValidated;
            bool approvedChangeSetValidated;
            bool changeSetFileReceiptsPersisted;
            bool separateValidationApproval;
            bool trustedValidationPassed;
            bool automaticContinuation;
            bool secondProposalPaused;
            bool rejectionStopped;
            await using (
                PiAgentDesktopRuntime runtime =
                    await PiAgentDesktopRuntime.StartAsync(
                        new PiAgentDesktopRuntimeOptions(
                            sidecarOptions,
                            fixtureRoot),
                        provider,
                        cancellationToken: cancellationToken))
            {
                PiAgentReviewedIterationCoordinator coordinator =
                    await PiAgentReviewedIterationCoordinator.OpenAsync(
                        runtime.Conversation,
                        fixtureRoot,
                        sidecarOptions,
                        store,
                        gate,
                        cancellationToken);
                PiAgentConversationTurn first = await coordinator.StartAsync(
                    "Improve the reviewed iteration fixture safely.",
                    cancellationToken);
                PiAgentConversationTurnSnapshot firstFinal =
                    await first.Completion.WaitAsync(cancellationToken);
                await coordinator.ObserveTurnCompletionAsync(
                    firstFinal,
                    cancellationToken);
                PiAgentReviewedIterationSnapshot firstPending =
                    coordinator.Snapshot ??
                    throw new InvalidOperationException(
                        "The first iteration state was absent.");
                PiAgentWorkspaceEditSnapshot firstProposal =
                    firstFinal.WorkspaceEdits.Single();
                firstProposalPaused =
                    firstPending.Status ==
                        PiAgentReviewedIterationStatus.AwaitingOwnerReview &&
                    firstPending.CurrentProposalId ==
                        firstProposal.ProposalId &&
                    firstProposal.Operation == "create" &&
                    !runtime.Conversation.Snapshot.CanSubmit;

                PiAgentReviewedIterationDecisionResult approved =
                    await coordinator.ApproveAndContinueAsync(
                        firstProposal.ProposalId,
                        cancellationToken);
                approvedValidated =
                    approved.Edit.Status ==
                        PiAgentWorkspaceEditStatus.Applied &&
                    approved.Iteration.ApprovedEditCount == 1 &&
                    approved.Iteration.Steps.Single().ValidationResult ==
                        "passed";
                approvedNewFileValidated =
                    approvedValidated &&
                    approved.Edit.Operation == "create" &&
                    approved.Edit.RelativePath == "generated.txt" &&
                    await File.ReadAllTextAsync(
                        createdFixturePath,
                        cancellationToken) ==
                        "owner-created\n";
                separateValidationApproval =
                    approved.ContinuedTurn is null &&
                    approved.Iteration.Status ==
                        PiAgentReviewedIterationStatus
                            .AwaitingTrustedValidation &&
                    approved.Iteration.Steps.Single()
                        .TrustedValidationResult == "pending" &&
                    !File.Exists(validationMarkerPath);
                PiAgentTrustedValidationDecisionResult firstValidation =
                    await coordinator
                        .RunTrustedValidationAndContinueAsync(
                            cancellationToken);
                trustedValidationPassed =
                    firstValidation.Validation is
                        { Passed: true, ExitCode: 0 } &&
                    firstValidation.Iteration.Steps.Single()
                        .TrustedValidationResult == "passed" &&
                    File.Exists(validationMarkerPath) &&
                    (await File.ReadAllLinesAsync(
                        validationMarkerPath,
                        cancellationToken)).Length == 1;
                automaticContinuation =
                    firstValidation.ContinuedTurn is not null &&
                    firstValidation.Iteration.Status ==
                        PiAgentReviewedIterationStatus.ActiveTurn;
                if (firstValidation.ContinuedTurn is null)
                {
                    throw new InvalidOperationException(
                        "The reviewed iteration did not continue after trusted validation: " +
                        firstValidation.Iteration.StatusDetail + " / " +
                        firstValidation.Iteration.Steps.Last()
                            .TrustedValidationResult +
                        " / " +
                        firstValidation.Iteration.Steps.Last().ErrorCode);
                }
                PiAgentConversationTurnSnapshot secondFinal =
                    await firstValidation.ContinuedTurn.Completion.WaitAsync(
                        cancellationToken);
                await coordinator.ObserveTurnCompletionAsync(
                    secondFinal,
                    cancellationToken);
                PiAgentReviewedIterationSnapshot secondPending =
                    coordinator.Snapshot ??
                    throw new InvalidOperationException(
                        "The second iteration state was absent.");
                PiAgentWorkspaceEditSnapshot secondProposal =
                    secondFinal.WorkspaceEdits.Single();
                secondProposalPaused =
                    secondPending.Status ==
                        PiAgentReviewedIterationStatus.AwaitingOwnerReview &&
                    secondPending.CurrentProposalId ==
                        secondProposal.ProposalId &&
                    secondProposal.Operation == "change-set" &&
                    secondProposal.FileChanges.Count == 3 &&
                    secondProposal.FileChanges.Select(file =>
                        file.RelativePath).SequenceEqual(
                            ["generated.txt", "review.txt", "docs/change.md"]);
                PiAgentReviewedIterationDecisionResult patchApproved =
                    await coordinator.ApproveAndContinueAsync(
                        secondProposal.ProposalId,
                        cancellationToken);
                approvedPatchValidated =
                    patchApproved.Edit.Status ==
                        PiAgentWorkspaceEditStatus.Applied &&
                    patchApproved.Edit.Operation == "change-set" &&
                    patchApproved.Edit.FileChanges.Single(file =>
                        file.Operation == "patch").PatchHunks.Count == 2 &&
                    patchApproved.Iteration.ApprovedEditCount == 2 &&
                    patchApproved.Iteration.Steps.Count == 2 &&
                    patchApproved.Iteration.Steps.Last().ValidationResult ==
                        "passed" &&
                    await File.ReadAllTextAsync(
                        createdFixturePath,
                        cancellationToken) ==
                        "OWNER-patched\n";
                approvedChangeSetValidated =
                    approvedPatchValidated &&
                    await File.ReadAllTextAsync(
                        fixturePath,
                        cancellationToken) ==
                        "ALPHA\nowner-reviewed\nOMEGA\n" &&
                    await File.ReadAllTextAsync(
                        Path.Combine(fixtureRoot, "docs", "change.md"),
                        cancellationToken) ==
                        "# Reviewed iteration change set\n";
                PiAgentReviewedIterationStepReceipt changeSetStep =
                    patchApproved.Iteration.Steps.Last();
                changeSetFileReceiptsPersisted =
                    changeSetStep.RelativePath == string.Empty &&
                    changeSetStep.Files.Count == 3 &&
                    changeSetStep.Files.All(file =>
                        file.AfterSha256 is { Length: 64 }) &&
                    changeSetStep.Files.Select(file => file.RelativePath)
                        .SequenceEqual(
                            ["generated.txt", "review.txt", "docs/change.md"]);
                separateValidationApproval =
                    separateValidationApproval &&
                    patchApproved.ContinuedTurn is null &&
                    patchApproved.Iteration.Status ==
                        PiAgentReviewedIterationStatus
                            .AwaitingTrustedValidation &&
                    patchApproved.Iteration.Steps.Last()
                        .TrustedValidationResult == "pending" &&
                    (await File.ReadAllLinesAsync(
                        validationMarkerPath,
                        cancellationToken)).Length == 1;
                PiAgentTrustedValidationDecisionResult secondValidation =
                    await coordinator
                        .RunTrustedValidationAndContinueAsync(
                            cancellationToken);
                trustedValidationPassed =
                    trustedValidationPassed &&
                    secondValidation.Validation is
                        { Passed: true, ExitCode: 0 } &&
                    secondValidation.Iteration.Steps.Last()
                        .TrustedValidationResult == "passed" &&
                    (await File.ReadAllLinesAsync(
                        validationMarkerPath,
                        cancellationToken)).Length == 2;
                automaticContinuation =
                    automaticContinuation &&
                    secondValidation.ContinuedTurn is not null &&
                    secondValidation.Iteration.Status ==
                        PiAgentReviewedIterationStatus.ActiveTurn;
                if (secondValidation.ContinuedTurn is null)
                {
                    throw new InvalidOperationException(
                        "The reviewed iteration did not continue after the second trusted validation.");
                }
                PiAgentConversationTurnSnapshot thirdFinal =
                    await secondValidation.ContinuedTurn.Completion.WaitAsync(
                        cancellationToken);
                await coordinator.ObserveTurnCompletionAsync(
                    thirdFinal,
                    cancellationToken);
                PiAgentWorkspaceEditSnapshot thirdProposal =
                    thirdFinal.WorkspaceEdits.Single();
                PiAgentReviewedIterationDecisionResult rejected =
                    await coordinator.RejectAsync(
                        thirdProposal.ProposalId,
                        cancellationToken);
                rejectionStopped =
                    thirdProposal.Operation == "replace" &&
                    thirdProposal.RelativePath == "generated.txt" &&
                    rejected.Edit.Status ==
                        PiAgentWorkspaceEditStatus.Rejected &&
                    rejected.Iteration.Status ==
                        PiAgentReviewedIterationStatus.Stopped &&
                    rejected.Iteration.Steps.Count == 3 &&
                    rejected.Iteration.ApprovedEditCount == 2;

                brokerRequests += runtime.BrokerRequestCount;
                brokerFaults += runtime.BrokerFaultCount;
                await runtime.ShutdownAsync(cancellationToken);
            }

            PiAgentReviewedIterationSnapshot durableFirst =
                await store.LoadLatestAsync(
                    fixtureRoot,
                    cancellationToken) ??
                throw new InvalidOperationException(
                    "The durable reviewed iteration receipt was absent.");
            bool durableRoundTrip =
                durableFirst.Status ==
                    PiAgentReviewedIterationStatus.Stopped &&
                durableFirst.Steps.Count == 3 &&
                durableFirst.ApprovedEditCount == 2 &&
                durableFirst.Steps.Take(2).All(step =>
                    step.TrustedValidationResult == "passed" &&
                    step.TrustedValidationReceiptDigest is not null &&
                    step.TrustedValidationOutputDigest is not null &&
                    step.TrustedValidationExitCode == 0) &&
                durableFirst.Steps[0].Files.Count == 1 &&
                durableFirst.Steps[1].Files.Count == 3;
            PiAgentReviewedIterationStepReceipt[] caseAliasedSteps =
                durableFirst.Steps.ToArray();
            PiAgentReviewedIterationFileReceipt[] caseAliasedFiles =
                caseAliasedSteps[1].Files.ToArray();
            caseAliasedFiles[1] = caseAliasedFiles[1] with
            {
                RelativePath =
                    caseAliasedFiles[0].RelativePath.ToUpperInvariant(),
            };
            caseAliasedSteps[1] = caseAliasedSteps[1] with
            {
                Files = caseAliasedFiles,
                AfterSha256 = ComputeChangeSetAfterDigest(
                    caseAliasedFiles),
            };
            bool caseAliasedFileReceiptsRejected = RejectsAdmission(
                durableFirst with { Steps = caseAliasedSteps });
            PiAgentReviewedIterationStepReceipt[] proposalFreeSteps =
                durableFirst.Steps.ToArray();
            proposalFreeSteps[2] = proposalFreeSteps[2] with
            {
                ProposalId = null,
                Files = [],
            };
            bool proposalFreeWorkspaceReceiptsRejected =
                RejectsAdmission(
                    durableFirst with { Steps = proposalFreeSteps });
            bool ciphertext = Directory.EnumerateFiles(
                    storeRoot,
                    "*.j2iteration",
                    SearchOption.AllDirectories)
                .All(path =>
                    !File.ReadAllText(path).Contains(
                        durableFirst.Mission,
                        StringComparison.Ordinal));

            await RunGitAsync(
                gitExecutable,
                fixtureRoot,
                [
                    "add",
                    "--",
                    "review.txt",
                    "generated.txt",
                    "docs/change.md",
                ],
                cancellationToken);
            await RunGitAsync(
                gitExecutable,
                fixtureRoot,
                ["commit", "--quiet", "-m", "probe approved edit"],
                cancellationToken);

            DiagnosticIterationModelProvider suspensionProvider = new(
                startAtReplacement: "owner-reviewed",
                createFirst: false);
            await using (
                PiAgentDesktopRuntime runtime =
                    await PiAgentDesktopRuntime.StartAsync(
                        new PiAgentDesktopRuntimeOptions(
                            sidecarOptions,
                            fixtureRoot),
                        suspensionProvider,
                        cancellationToken: cancellationToken))
            {
                PiAgentReviewedIterationCoordinator coordinator =
                    await PiAgentReviewedIterationCoordinator.OpenAsync(
                        runtime.Conversation,
                        fixtureRoot,
                        sidecarOptions,
                        store,
                        gate,
                        cancellationToken);
                PiAgentConversationTurn turn = await coordinator.StartAsync(
                    "Continue the fixture through an explicit restart boundary.",
                    cancellationToken);
                PiAgentConversationTurnSnapshot terminal =
                    await turn.Completion.WaitAsync(cancellationToken);
                await coordinator.ObserveTurnCompletionAsync(
                    terminal,
                    cancellationToken);
                await coordinator.SuspendAsync(cancellationToken);
                brokerRequests += runtime.BrokerRequestCount;
                brokerFaults += runtime.BrokerFaultCount;
                await runtime.ShutdownAsync(cancellationToken);
            }

            PiAgentReviewedIterationSnapshot suspended =
                await store.LoadLatestAsync(
                    fixtureRoot,
                    cancellationToken) ??
                throw new InvalidOperationException(
                    "The suspended reviewed iteration receipt was absent.");
            bool shutdownSuspension =
                suspended.Status ==
                    PiAgentReviewedIterationStatus.Interrupted &&
                suspended.CurrentProposalId is null &&
                suspended.CurrentTurnId is null;

            bool restartNoCapability;
            bool explicitRearm;
            DiagnosticIterationModelProvider resumeProvider = new(
                startAtReplacement: "owner-reviewed",
                createFirst: false);
            await using (
                PiAgentDesktopRuntime runtime =
                    await PiAgentDesktopRuntime.StartAsync(
                        new PiAgentDesktopRuntimeOptions(
                            sidecarOptions,
                            fixtureRoot),
                        resumeProvider,
                        cancellationToken: cancellationToken))
            {
                PiAgentReviewedIterationCoordinator coordinator =
                    await PiAgentReviewedIterationCoordinator.OpenAsync(
                        runtime.Conversation,
                        fixtureRoot,
                        sidecarOptions,
                        store,
                        gate,
                        cancellationToken);
                restartNoCapability =
                    coordinator.Snapshot?.Status ==
                        PiAgentReviewedIterationStatus.Interrupted &&
                    runtime.Conversation.Snapshot.Turns
                        .SelectMany(turn => turn.WorkspaceEdits)
                        .Count() == 0 &&
                    runtime.Conversation.Snapshot.CanSubmit;
                PiAgentConversationTurn resumed =
                    await coordinator.ResumeAsync(cancellationToken) ??
                    throw new InvalidOperationException(
                        "Explicit re-arm did not start a bounded turn.");
                PiAgentConversationTurnSnapshot resumedFinal =
                    await resumed.Completion.WaitAsync(cancellationToken);
                await coordinator.ObserveTurnCompletionAsync(
                    resumedFinal,
                    cancellationToken);
                PiAgentWorkspaceEditSnapshot proposal =
                    resumedFinal.WorkspaceEdits.Single();
                explicitRearm =
                    coordinator.Snapshot?.Status ==
                        PiAgentReviewedIterationStatus.AwaitingOwnerReview &&
                    proposal.Status == PiAgentWorkspaceEditStatus.Pending;
                _ = await coordinator.RejectAsync(
                    proposal.ProposalId,
                    cancellationToken);
                brokerRequests += runtime.BrokerRequestCount;
                brokerFaults += runtime.BrokerFaultCount;
                await runtime.ShutdownAsync(cancellationToken);
            }

            PiAgentReviewedIterationSnapshot latest =
                await store.LoadLatestAsync(
                    fixtureRoot,
                    cancellationToken) ??
                throw new InvalidOperationException(
                    "The final reviewed iteration receipt was absent.");
            Dictionary<string, string> expected = new(StringComparer.Ordinal)
            {
                ["review.txt"] = new string('0', 64),
            };
            PiAgentRepositoryValidationReceipt drift =
                await gate.ValidateAsync(
                    fixtureRoot,
                    latest.RepositoryHead,
                    expected,
                    cancellationToken);
            bool repositoryDriftRejected =
                !drift.Passed && drift.RepositoryDigest is null;
            await File.WriteAllTextAsync(
                Path.Combine(fixtureRoot, "trusted-cancellation.test.mjs"),
                """
                import { writeFile } from "node:fs/promises";
                import { tmpdir } from "node:os";
                import { basename, join } from "node:path";

                const prefix = join(
                  tmpdir(),
                  `jarvis-reviewed-validation-${basename(process.cwd())}`,
                );
                await writeFile(`${prefix}-cancel-started.marker`, "started\n", "utf8");
                await new Promise((resolve) => setTimeout(resolve, 60_000));
                await writeFile(`${prefix}-cancel-completed.marker`, "completed\n", "utf8");
                """ + "\n",
                new UTF8Encoding(false),
                cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(
                    fixtureRoot,
                    PiAgentReviewedIterationTrustedValidator
                        .ManifestRelativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)),
                """
                {
                  "schemaVersion": 1,
                  "profileId": "jarvisv2-reviewed-cancellation-v1",
                  "runner": "node-test",
                  "timeoutSeconds": 120,
                  "testFiles": ["trusted-cancellation.test.mjs"]
                }
                """ + "\n",
                new UTF8Encoding(false),
                cancellationToken);
            await RunGitAsync(
                gitExecutable,
                fixtureRoot,
                [
                    "add",
                    "--",
                    "config/pi-agent-trusted-validation.json",
                    "trusted-cancellation.test.mjs",
                ],
                cancellationToken);
            await RunGitAsync(
                gitExecutable,
                fixtureRoot,
                ["commit", "--quiet", "-m", "probe cancellation profile"],
                cancellationToken);
            PiAgentRepositoryBaselineReceipt cancellationBaseline =
                await gate.CaptureCleanBaselineAsync(
                    fixtureRoot,
                    cancellationToken);
            PiAgentReviewedIterationTrustedValidator cancellationValidator =
                new(sidecarOptions.NodeExecutablePath, gate);
            PiAgentTrustedValidationProfileReceipt cancellationProfile =
                await cancellationValidator.CaptureProfileAsync(
                    fixtureRoot,
                    cancellationBaseline.Head,
                    cancellationToken);
            PiAgentTrustedValidationReceipt modifiedTestReceipt =
                await cancellationValidator.RunAsync(
                    fixtureRoot,
                    cancellationBaseline.Head,
                    cancellationProfile.ProfileId,
                    cancellationProfile.ProfileDigest,
                    ["trusted-cancellation.test.mjs"],
                    cancellationToken);
            bool modifiedTrustedTestRejected =
                !modifiedTestReceipt.Passed &&
                modifiedTestReceipt.ErrorCode ==
                    "trusted-validation-test-file-modified" &&
                !File.Exists(validationCancelStartedPath);
            Task<PiAgentTrustedValidationReceipt> cancellationRun =
                cancellationValidator.RunAsync(
                    fixtureRoot,
                    cancellationBaseline.Head,
                    cancellationProfile.ProfileId,
                    cancellationProfile.ProfileDigest,
                    [],
                    cancellationToken);
            for (
                int attempt = 0;
                attempt < 40 && !File.Exists(validationCancelStartedPath);
                attempt++)
            {
                await Task.Delay(50, cancellationToken);
            }
            cancellationValidator.CancelActiveRun();
            PiAgentTrustedValidationReceipt cancellationReceipt =
                await cancellationRun.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
            await Task.Delay(200, cancellationToken);
            bool trustedValidationCancellation =
                File.Exists(validationCancelStartedPath) &&
                !File.Exists(validationCancelCompletedPath) &&
                !cancellationReceipt.Passed &&
                cancellationReceipt.ErrorCode ==
                    "trusted-validation-cancelled";
            int receiptCount = Directory.EnumerateFiles(
                    storeRoot,
                    "*.j2iteration",
                    SearchOption.AllDirectories)
                .Count();

            bool ownerPolicy =
                durableFirst.MaximumApprovedEdits ==
                    PiAgentReviewedIterationCoordinator.MaximumApprovedEdits &&
                durableFirst.ExpiresAtUtc - durableFirst.StartedAtUtc ==
                    TimeSpan.FromHours(
                        PiAgentReviewedIterationCoordinator.PolicyLifetimeHours) &&
                durableFirst.SchemaVersion == 3 &&
                !durableFirst.AutoContinueAfterApproval &&
                durableFirst.TrustedValidationProfileId ==
                    "jarvisv2-reviewed-fixture-v1";
            bool passed =
                cleanBaselineRequired &&
                durableRoundTrip &&
                ciphertext &&
                ownerPolicy &&
                firstProposalPaused &&
                approvedValidated &&
                approvedNewFileValidated &&
                approvedPatchValidated &&
                approvedChangeSetValidated &&
                changeSetFileReceiptsPersisted &&
                caseAliasedFileReceiptsRejected &&
                proposalFreeWorkspaceReceiptsRejected &&
                untrackedWhitespaceRejected &&
                separateValidationApproval &&
                trustedValidationPassed &&
                modifiedTrustedTestRejected &&
                trustedValidationCancellation &&
                automaticContinuation &&
                secondProposalPaused &&
                rejectionStopped &&
                shutdownSuspension &&
                restartNoCapability &&
                explicitRearm &&
                repositoryDriftRejected &&
                brokerFaults == 0 &&
                receiptCount == 2;
            return new PiAgentReviewedIterationProbeReceipt(
                1,
                "jarvisv2-pi-agent-reviewed-iteration-probe",
                passed ? "passed" : "failed",
                cleanBaselineRequired,
                durableRoundTrip,
                ciphertext,
                ownerPolicy,
                firstProposalPaused,
                approvedValidated,
                approvedNewFileValidated,
                approvedPatchValidated,
                approvedChangeSetValidated,
                changeSetFileReceiptsPersisted,
                caseAliasedFileReceiptsRejected,
                proposalFreeWorkspaceReceiptsRejected,
                untrackedWhitespaceRejected,
                separateValidationApproval,
                trustedValidationPassed,
                modifiedTrustedTestRejected,
                trustedValidationCancellation,
                automaticContinuation,
                secondProposalPaused,
                rejectionStopped,
                shutdownSuspension,
                restartNoCapability,
                explicitRearm,
                repositoryDriftRejected,
                false,
                false,
                false,
                PiAgentReviewedIterationCoordinator.MaximumApprovedEdits,
                PiAgentReviewedIterationCoordinator.PolicyLifetimeHours,
                receiptCount,
                brokerRequests,
                brokerFaults,
                "not-run",
                false);
        }
        finally
        {
            if (File.Exists(validationMarkerPath))
            {
                File.Delete(validationMarkerPath);
            }
            if (File.Exists(validationCancelStartedPath))
            {
                File.Delete(validationCancelStartedPath);
            }
            if (File.Exists(validationCancelCompletedPath))
            {
                File.Delete(validationCancelCompletedPath);
            }
            DeleteProbeDirectory(fixtureRoot, ".jarvis-reviewed-iteration-");
            DeleteProbeDirectory(
                storeRoot,
                ".jarvis-reviewed-iteration-store-");
        }
    }

    private static bool RejectsAdmission(
        PiAgentReviewedIterationSnapshot snapshot)
    {
        try
        {
            _ = PiAgentReviewedIterationAdmission.Admit(snapshot);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static string ComputeChangeSetAfterDigest(
        IReadOnlyList<PiAgentReviewedIterationFileReceipt> files)
    {
        StringBuilder material = new(
            "JARVIS2/workspace-change-set-after/v1");
        foreach (PiAgentReviewedIterationFileReceipt file in files)
        {
            material
                .Append('\0')
                .Append(file.Ordinal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
                .Append('\0')
                .Append(file.RelativePath)
                .Append('\0')
                .Append(file.AfterSha256);
        }
        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(material.ToString())))
            .ToLowerInvariant();
    }

    private static async Task RunGitAsync(
        string gitExecutable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.GetFullPath(gitExecutable),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "The reviewed iteration probe could not start Git.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        _ = await outputTask;
        string error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The reviewed iteration Git fixture failed with exit code {process.ExitCode}: {error.Trim()}");
        }
    }

    private static string ResolveWorkspaceRoot(
        PiAgentSidecarOptions sidecarOptions)
    {
        DirectoryInfo? directory = new(
            Path.GetDirectoryName(sidecarOptions.HostScriptPath) ??
            throw new InvalidOperationException(
                "The sidecar host path had no parent directory."));
        while (directory is not null)
        {
            if (
                Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "The reviewed iteration probe could not locate the workspace root.");
    }

    private static void DeleteProbeDirectory(
        string path,
        string requiredPrefix)
    {
        string fullPath = Path.GetFullPath(path);
        if (
            !Path.GetFileName(fullPath).StartsWith(
                requiredPrefix,
                StringComparison.Ordinal) ||
            Path.GetDirectoryName(fullPath) is null)
        {
            throw new InvalidOperationException(
                "The reviewed iteration probe cleanup target was not admitted.");
        }
        if (Directory.Exists(fullPath))
        {
            foreach (string file in Directory.EnumerateFiles(
                         fullPath,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            foreach (string directory in Directory.EnumerateDirectories(
                         fullPath,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(directory, FileAttributes.Directory);
            }
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private sealed class DiagnosticIterationModelProvider(
        string startAtReplacement = "owner-reviewed",
        bool createFirst = true) :
        IDesktopModelProvider
    {
        private int requestSequence;
        private string currentText = startAtReplacement;
        private readonly bool createFirst = createFirst;

        public async IAsyncEnumerable<DesktopModelStreamEvent> StreamAsync(
            DesktopModelBrokerRequest request,
            [EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            int sequence = Interlocked.Increment(ref requestSequence);
            if (sequence % 2 == 0)
            {
                yield return new DesktopModelTextDelta(
                    "JARVIS paused at the reviewed owner boundary.");
                yield return new DesktopModelCompleted(
                    "stop",
                    new DesktopModelUsage(12, 8, 0, 0));
                yield break;
            }
            if (createFirst && sequence == 1)
            {
                string createToolCallId =
                    $"reviewed-iteration-create-{sequence}";
                yield return new DesktopModelToolCallStarted(
                    createToolCallId,
                    "propose_create_file");
                yield return new DesktopModelToolCallDelta(
                    createToolCallId,
                    JsonSerializer.Serialize(new
                    {
                        path = "generated.txt",
                        content = "owner-created\n",
                    }));
                yield return new DesktopModelToolCallCompleted(
                    createToolCallId);
                currentText = "owner-created";
                yield return new DesktopModelCompleted(
                    "toolUse",
                    new DesktopModelUsage(14, 10, 0, 0));
                yield break;
            }
            if (createFirst && sequence == 3)
            {
                string patchToolCallId =
                    $"reviewed-iteration-change-set-{sequence}";
                yield return new DesktopModelToolCallStarted(
                    patchToolCallId,
                    "propose_change_set");
                yield return new DesktopModelToolCallDelta(
                    patchToolCallId,
                    JsonSerializer.Serialize(new
                    {
                        changes = new object[]
                        {
                            new
                            {
                                operation = "replace",
                                path = "generated.txt",
                                oldText = "owner-created",
                                newText = "OWNER-patched",
                            },
                            new
                            {
                                operation = "patch",
                                path = "review.txt",
                                replacements = new[]
                                {
                                    new
                                    {
                                        oldText = "alpha",
                                        newText = "ALPHA",
                                    },
                                    new
                                    {
                                        oldText = "omega",
                                        newText = "OMEGA",
                                    },
                                },
                            },
                            new
                            {
                                operation = "create",
                                path = "docs/change.md",
                                content = "# Reviewed iteration change set\n",
                            },
                        },
                    }));
                yield return new DesktopModelToolCallCompleted(
                    patchToolCallId);
                currentText = "OWNER-patched";
                yield return new DesktopModelCompleted(
                    "toolUse",
                    new DesktopModelUsage(16, 12, 0, 0));
                yield break;
            }
            string replacement = currentText switch
            {
                "owner-reviewed" => "owner-approved",
                "owner-approved" => "owner-second",
                _ => "owner-next",
            };
            string toolCallId = $"reviewed-iteration-edit-{sequence}";
            yield return new DesktopModelToolCallStarted(
                toolCallId,
                "propose_edit");
            yield return new DesktopModelToolCallDelta(
                toolCallId,
                JsonSerializer.Serialize(new
                {
                    path = createFirst
                        ? "generated.txt"
                        : "review.txt",
                    oldText = currentText,
                    newText = replacement,
                }));
            yield return new DesktopModelToolCallCompleted(toolCallId);
            currentText = replacement;
            yield return new DesktopModelCompleted(
                "toolUse",
                new DesktopModelUsage(14, 10, 0, 0));
        }
    }
}
