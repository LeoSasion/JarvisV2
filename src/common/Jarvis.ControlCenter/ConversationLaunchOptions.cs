namespace Jarvis.ControlCenter;

public sealed record ConversationLaunchOptions(
    string NodeExecutablePath,
    string SidecarHostPath,
    string WorkspaceRoot);
