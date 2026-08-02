namespace Jarvis.ControlCenter;

public enum ConversationProviderKind
{
    LocalDiagnostic,
    OpenAiResponses,
}

public sealed record ConversationLaunchOptions(
    string NodeExecutablePath,
    string SidecarHostPath,
    string WorkspaceRoot,
    ConversationProviderKind Provider =
        ConversationProviderKind.LocalDiagnostic)
{
    public string ProviderDisplayName => Provider switch
    {
        ConversationProviderKind.OpenAiResponses =>
            Jarvis.PiAgentHost.OpenAiResponsesModelProvider.DisplayName,
        _ => LocalDiagnosticModelProvider.DisplayName,
    };
}
