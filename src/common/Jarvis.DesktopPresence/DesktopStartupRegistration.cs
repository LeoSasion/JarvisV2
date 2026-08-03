using System.IO;
using Microsoft.Win32;

namespace Jarvis.DesktopPresence;

public enum DesktopStartupRegistrationState
{
    Disabled,
    EnabledForCurrentExecutable,
    EnabledForDifferentExecutable,
}

public sealed record DesktopStartupRegistrationReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    DesktopStartupRegistrationState State,
    bool Enabled,
    bool CurrentExecutable,
    bool Changed,
    string ExpectedCommand,
    string? RegisteredCommand,
    string PersistenceModel);

public sealed class DesktopStartupRegistration
{
    public const string PersistenceModel =
        "current-user-run-key-exact-reg-sz-no-shell";
    public const string ValueName = "JarvisV2";
    public const string SubKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly IDesktopStartupValueStore store;

    public DesktopStartupRegistration()
        : this(new CurrentUserRunValueStore())
    {
    }

    internal DesktopStartupRegistration(IDesktopStartupValueStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public DesktopStartupRegistrationReceipt Inspect(string executablePath)
    {
        string command = CreateCommand(executablePath);
        string? registeredCommand = store.Read();
        DesktopStartupRegistrationState state = registeredCommand switch
        {
            null => DesktopStartupRegistrationState.Disabled,
            _ when string.Equals(
                registeredCommand,
                command,
                StringComparison.Ordinal) =>
                DesktopStartupRegistrationState.EnabledForCurrentExecutable,
            _ => DesktopStartupRegistrationState.EnabledForDifferentExecutable,
        };
        return CreateReceipt(
            state,
            changed: false,
            command,
            registeredCommand);
    }

    public DesktopStartupRegistrationReceipt SetEnabled(
        string executablePath,
        bool enabled)
    {
        string command = CreateCommand(executablePath);
        string? registeredCommand = store.Read();
        bool changed;
        if (enabled)
        {
            changed = !string.Equals(
                registeredCommand,
                command,
                StringComparison.Ordinal);
            if (changed)
            {
                store.Write(command);
            }
            registeredCommand = command;
        }
        else
        {
            changed = registeredCommand is not null;
            if (changed)
            {
                store.Delete();
            }
            registeredCommand = null;
        }

        DesktopStartupRegistrationState state = enabled
            ? DesktopStartupRegistrationState.EnabledForCurrentExecutable
            : DesktopStartupRegistrationState.Disabled;
        return CreateReceipt(state, changed, command, registeredCommand);
    }

    public static string CreateCommand(string executablePath)
    {
        string path = AdmitExecutablePath(executablePath);
        return $"\"{path}\" --resume-latest --minimized";
    }

    private static string AdmitExecutablePath(string executablePath)
    {
        if (
            string.IsNullOrWhiteSpace(executablePath) ||
            !Path.IsPathFullyQualified(executablePath) ||
            executablePath.Length > 1_024 ||
            executablePath.IndexOfAny(['\r', '\n', '"']) >= 0)
        {
            throw new ArgumentException(
                "Desktop startup requires one absolute executable path.",
                nameof(executablePath));
        }
        string path = Path.GetFullPath(executablePath);
        if (
            !string.Equals(
                Path.GetExtension(path),
                ".exe",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path) ||
            Directory.Exists(path))
        {
            throw new ArgumentException(
                "Desktop startup requires one existing Windows executable.",
                nameof(executablePath));
        }
        FileInfo file = new(path);
        if (
            (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
            file.Length < 2)
        {
            throw new InvalidDataException(
                "The desktop executable failed file admission.");
        }
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 2,
            FileOptions.SequentialScan);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
        {
            throw new InvalidDataException(
                "The desktop executable is not a Windows PE image.");
        }
        return path;
    }

    private static DesktopStartupRegistrationReceipt CreateReceipt(
        DesktopStartupRegistrationState state,
        bool changed,
        string expectedCommand,
        string? registeredCommand) =>
        new(
            1,
            "jarvisv2-desktop-startup-registration",
            "passed",
            state,
            state != DesktopStartupRegistrationState.Disabled,
            state ==
                DesktopStartupRegistrationState.EnabledForCurrentExecutable,
            changed,
            expectedCommand,
            registeredCommand,
            PersistenceModel);

    internal interface IDesktopStartupValueStore
    {
        string? Read();

        void Write(string command);

        void Delete();
    }

    private sealed class CurrentUserRunValueStore : IDesktopStartupValueStore
    {
        public string? Read()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                SubKeyPath,
                writable: false);
            if (
                key is null ||
                !key.GetValueNames().Any(name =>
                    string.Equals(
                        name,
                        ValueName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            if (key.GetValueKind(ValueName) != RegistryValueKind.String)
            {
                throw new InvalidDataException(
                    "The JarvisV2 startup value is not an exact REG_SZ value.");
            }
            return key.GetValue(
                    ValueName,
                    defaultValue: null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames)
                as string ??
                throw new InvalidDataException(
                    "The JarvisV2 startup value is not a string.");
        }

        public void Write(string command)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    SubKeyPath,
                    writable: true) ??
                throw new IOException(
                    "The current-user startup key could not be opened.");
            key.SetValue(ValueName, command, RegistryValueKind.String);
            key.Flush();
        }

        public void Delete()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                SubKeyPath,
                writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            key?.Flush();
        }
    }
}
