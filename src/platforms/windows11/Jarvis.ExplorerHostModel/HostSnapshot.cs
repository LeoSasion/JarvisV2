namespace Jarvis.ExplorerHostModel;

internal sealed class HostSnapshot
{
    public int SchemaVersion { get; init; }
    public string EvidenceKind { get; init; } = string.Empty;
    public bool LiveSystemTouched { get; init; }
    public int CurrentSessionId { get; init; }
    public string KillSwitchState { get; init; } = string.Empty;
    public string ActiveModulePermitState { get; init; } = string.Empty;
    public LegacyHostSnapshot LegacyHost { get; init; } = new();
    public TargetSelectionSnapshot Selection { get; init; } = new();
    public TargetProcessSnapshot Target { get; init; } = new();
    public CandidateModuleSnapshot Module { get; init; } = new();
    public IReadOnlyList<ExistingMappingSnapshot> ExistingMappings { get; init; } =
        Array.Empty<ExistingMappingSnapshot>();
}

internal sealed class LegacyHostSnapshot
{
    public bool Quarantined { get; init; }
    public string ServiceState { get; init; } = string.Empty;
    public int ServiceProcessId { get; init; }
    public int BaseRuntimeMappingCount { get; init; }
}

internal sealed class TargetSelectionSnapshot
{
    public string Mode { get; init; } = string.Empty;
    public bool ProcessEnumerationPerformed { get; init; }
    public bool ShellWindowPresent { get; init; }
    public int ShellWindowProcessId { get; init; }
    public int ShellWindowThreadId { get; init; }
    public int DesktopShellCandidateCount { get; init; }
}

internal sealed class TargetProcessSnapshot
{
    public int ProcessId { get; init; }
    public int SessionId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string ImagePath { get; init; } = string.Empty;
    public string ExpectedImagePath { get; init; } = string.Empty;
    public string ImageSha256 { get; init; } = string.Empty;
    public string ExpectedImageSha256 { get; init; } = string.Empty;
    public string ProductVersion { get; init; } = string.Empty;
    public string ExpectedProductVersion { get; init; } = string.Empty;
    public string SignatureState { get; init; } = string.Empty;
    public string SignerSubject { get; init; } = string.Empty;
    public string ExpectedSignerSubject { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string StartTimeUtc { get; init; } = string.Empty;
}

internal sealed class CandidateModuleSnapshot
{
    public string ModuleId { get; init; } = string.Empty;
    public string Contract { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string ExpectedSha256 { get; init; } = string.Empty;
    public string SignatureState { get; init; } = string.Empty;
    public string SignerSubject { get; init; } = string.Empty;
    public string ExpectedSignerSubject { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
}

internal sealed class ExistingMappingSnapshot
{
    public int ProcessId { get; init; }
    public string ModuleName { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
