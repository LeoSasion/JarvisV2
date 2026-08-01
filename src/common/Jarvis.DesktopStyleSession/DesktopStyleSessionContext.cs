namespace Jarvis.DesktopStyleSession;

public sealed class DesktopStyleSessionContext
{
    private const string SharedProjectPath =
        @".\src\common\Jarvis.DesktopStyleSession";

    private const string Windows10ProjectPath =
        @".\src\platforms\windows10\Jarvis.Win10.DesktopStyleSession";

    private DesktopStyleSessionContext(
        string commandProjectPath,
        bool commandRequiresExpectedExplorerProcessId,
        string? hostProfileId)
    {
        CommandProjectPath = commandProjectPath;
        CommandRequiresExpectedExplorerProcessId =
            commandRequiresExpectedExplorerProcessId;
        HostProfileId = hostProfileId;
    }

    public string CommandProjectPath { get; }

    public bool CommandRequiresExpectedExplorerProcessId { get; }

    public string? HostProfileId { get; }

    public static DesktopStyleSessionContext Shared { get; } =
        new(SharedProjectPath, true, null);

    public static DesktopStyleSessionContext ForExactWindows10Host(
        string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (!profileId.StartsWith("win10-", StringComparison.Ordinal) ||
            !profileId.EndsWith("-x64", StringComparison.Ordinal) ||
            profileId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '-' or '.')))
        {
            throw new ArgumentException(
                "The Windows 10 host profile id is not canonical.",
                nameof(profileId));
        }

        return new DesktopStyleSessionContext(
            Windows10ProjectPath,
            false,
            profileId);
    }
}
