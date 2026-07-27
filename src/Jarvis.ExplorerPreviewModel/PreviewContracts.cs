namespace Jarvis.ExplorerPreviewModel;

internal static class PreviewContract
{
    public const string ProfileId =
        "win11-25h2-26200.8875-x64-explorer-frame-candidate-v1";
    public const string HostProfileId =
        "win11-25h2-26200.8875-x64";
    public const string UpstreamName =
        "Windows 11 File Explorer Styler";
    public const string UpstreamVersion = "1.5";
    public const string UpstreamCommit =
        "109589023dde428deaee2fe80e4ce446283a7935";
    public const string UpstreamSourceSha256 =
        "ECD6189A76439518E84938F4CA42FDB7F78AA1CCE3151EE0FE93638918D2DCED";
    public const string UpstreamGitBlob =
        "6f67b714c271db1235a5f937c30c5cae55b180bf";
    public const int UpstreamSourceSize = 326922;

    public static readonly IReadOnlySet<string> RequiredRoles =
        new HashSet<string>(
            ["tab-strip", "command-bar", "navigation-pane"],
            StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> AllowedProperties =
        new HashSet<string>(
            ["Background", "Foreground", "BorderBrush"],
            StringComparer.Ordinal);
}

internal sealed record CandidateProfileDocument(
    int SchemaVersion,
    string ProfileId,
    string LifecycleState,
    string HostProfileId,
    HostFingerprint HostFingerprint,
    UpstreamIdentity UpstreamIdentity,
    SurfaceCandidate[] Surfaces,
    string[] AllowedProperties,
    PreviewPolicy PreviewPolicy,
    string LiveEvidence,
    bool ExecutionSupported,
    bool ActivationPermitted,
    bool MutationPerformed);

internal sealed record HostFingerprint(
    int WindowsBuild,
    int Ubr,
    string Architecture,
    string ExplorerProductVersion,
    long ExplorerSize,
    string ExplorerSha256);

internal sealed record UpstreamIdentity(
    string Name,
    string Version,
    string Repository,
    string Commit,
    string SourcePath,
    string GitBlob,
    int SourceSize,
    string SourceSha256,
    string License);

internal sealed record SurfaceCandidate(
    string Role,
    string Selector,
    int ExpectedMatchCount,
    string EvidenceState);

internal sealed record PreviewPolicy(
    int DurationSeconds,
    bool RequireSeparateExplorerProcess,
    bool RequireCompleteOriginalSnapshot,
    string RestoreOrder,
    string[] ScreenshotCheckpoints,
    bool CloseTemporaryWindowAfterRestore);

internal sealed record CompatibilityDocument(
    CompatibilityHost[] ValidatedHosts);

internal sealed record CompatibilityHost(
    string ProfileId,
    int WindowsBuild,
    int Ubr,
    string Architecture,
    CompatibilityExplorer Explorer);

internal sealed record CompatibilityExplorer(
    string ProductVersion,
    long Size,
    string Sha256);

internal sealed record CompiledSurfaceCandidate(
    string Role,
    string Selector,
    string SelectorFingerprint,
    int ExpectedMatchCount,
    string EvidenceState);

internal sealed record CandidateCompilationReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string ProfileId,
    string ProfileSha256,
    string CompatibilitySha256,
    IReadOnlyList<CompiledSurfaceCandidate> Surfaces,
    bool ReadyForReadOnlyDiscovery,
    bool ReadyForPreview,
    bool ReadyForExactApproval,
    bool ExecutionSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

internal sealed record ObservedTarget(
    int ProcessId,
    int DesktopShellProcessId,
    int ThreadId,
    string WindowHandle,
    string WindowClass,
    string WindowTitle,
    string ExpectedWindowTitle,
    bool SeparateProcess,
    DateTime ProcessStartTimeUtc,
    string VisualTreeGeneration);

internal sealed record ObservedSurface(
    string Role,
    string Selector,
    int MatchCount,
    string InstanceId,
    IReadOnlyDictionary<string, string> OriginalValues);

internal sealed record ReadOnlyDiscoveryEvidence(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string ProfileId,
    string ProfileSha256,
    DateTime ObservedAtUtc,
    ObservedTarget Target,
    ObservedSurface[] Surfaces,
    string LiveExplorer,
    bool MutationPerformed);

internal sealed record PreviewPlanStep(
    int Index,
    string Action,
    string? Role,
    bool FutureMutation,
    bool RequiresJournal);

internal sealed record PreviewReviewPlanReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string? PlanId,
    DateTime? ExpiresAtUtc,
    int PreviewDurationSeconds,
    IReadOnlyList<PreviewPlanStep> Steps,
    bool ReadyForExactApproval,
    bool ExecutionSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

internal sealed record ModelScenarioResult(
    string Name,
    bool Passed,
    string Detail);

internal sealed record PreviewModelTestReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    bool ExecutionSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<ModelScenarioResult> Scenarios);
