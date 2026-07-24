using System.Security;
using System.Text;
using System.Text.Json;

namespace Jarvis.Supervisor;

internal static class KillSwitch
{
    public const string StateGateName = @"Local\JARVIS2.StateGate.v1";

    private const string FlagFileName = "disabled.flag";
    private const string ActiveModuleFileName = "active-module.txt";
    private static readonly TimeSpan StateGateTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ActivationPermitLifetime =
        TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> AllowedModuleIdSet =
        new(StringComparer.Ordinal)
        {
            "jarvis-taskbar-icon-size",
        };

    public static string StateDirectory { get; } = ResolveStateDirectory();

    public static string FlagPath { get; } = Path.Combine(StateDirectory, FlagFileName);

    public static string ActiveModulePath { get; } =
        Path.Combine(StateDirectory, ActiveModuleFileName);

    public static IReadOnlySet<string> AllowedModuleIds => AllowedModuleIdSet;

    public static StateGateLease AcquireStateGate() =>
        StateGateLease.Acquire(StateGateName, StateGateTimeout);

    public static bool IsAllowedModuleId(string moduleId) =>
        AllowedModuleIdSet.Contains(moduleId);

    public static KillSwitchProbe Probe()
    {
        FilePresenceProbe presence = ProbePath(FlagPath);
        return presence.State switch
        {
            SafetyFileState.Present => new KillSwitchProbe(
                KillSwitchState.Armed,
                FlagPath,
                null),
            SafetyFileState.Absent => new KillSwitchProbe(
                KillSwitchState.Disarmed,
                FlagPath,
                null),
            _ => new KillSwitchProbe(
                KillSwitchState.Unknown,
                FlagPath,
                presence.Error),
        };
    }

    public static ActiveModuleProbe ProbeActiveModule()
    {
        FilePresenceProbe presence = ProbePath(ActiveModulePath);
        if (presence.State == SafetyFileState.Absent)
        {
            return new ActiveModuleProbe(
                ActiveModuleState.Absent,
                ActiveModulePath,
                null,
                null,
                null,
                null);
        }

        if (presence.State == SafetyFileState.Unknown)
        {
            return new ActiveModuleProbe(
                ActiveModuleState.Unknown,
                ActiveModulePath,
                null,
                null,
                null,
                presence.Error);
        }

        try
        {
            using FileStream stream = new(
                ActiveModulePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128,
                FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > 128)
            {
                return new ActiveModuleProbe(
                    ActiveModuleState.Invalid,
                    ActiveModulePath,
                    null,
                    null,
                    null,
                    "The active-module permit length was invalid.");
            }

            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            DateTimeOffset lastWriteTimeUtc = new(
                File.GetLastWriteTimeUtc(ActiveModulePath),
                TimeSpan.Zero);
            DateTimeOffset expiresAtUtc =
                lastWriteTimeUtc + ActivationPermitLifetime;
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (lastWriteTimeUtc > now)
            {
                return new ActiveModuleProbe(
                    ActiveModuleState.Invalid,
                    ActiveModulePath,
                    null,
                    lastWriteTimeUtc,
                    expiresAtUtc,
                    "The active-module permit was future-dated.");
            }

            if (now > expiresAtUtc)
            {
                return new ActiveModuleProbe(
                    ActiveModuleState.Invalid,
                    ActiveModulePath,
                    null,
                    lastWriteTimeUtc,
                    expiresAtUtc,
                    "The active-module permit expired; run arm-kill-switch before preparing another activation.");
            }

            if (bytes.Any(value => value > 0x7F))
            {
                return new ActiveModuleProbe(
                    ActiveModuleState.Invalid,
                    ActiveModulePath,
                    null,
                    lastWriteTimeUtc,
                    expiresAtUtc,
                    "The active-module permit was not a non-empty ASCII module id.");
            }

            string moduleId = Encoding.ASCII.GetString(bytes);
            if (!IsAllowedModuleId(moduleId))
            {
                return new ActiveModuleProbe(
                    ActiveModuleState.Invalid,
                    ActiveModulePath,
                    moduleId,
                    lastWriteTimeUtc,
                    expiresAtUtc,
                    "The active-module permit did not contain an allowlisted id.");
            }

            return new ActiveModuleProbe(
                ActiveModuleState.Valid,
                ActiveModulePath,
                moduleId,
                lastWriteTimeUtc,
                expiresAtUtc,
                null);
        }
        catch (Exception exception) when (IsFileProbeException(exception))
        {
            return new ActiveModuleProbe(
                ActiveModuleState.Unknown,
                ActiveModulePath,
                null,
                null,
                null,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public static KillSwitchResult ArmUnderLease(StateGateLease lease)
    {
        EnsureLeaseHeld(lease);
        bool flagChanged = EnsureFlagArmedUnderLease();

        // Keep the confirmed flag open without delete sharing until the permit has
        // been removed. A concurrent manual delete therefore can't expose a permit.
        using KillSwitchGuard guard = OpenArmedGuardUnderLease(lease);
        bool permitRemoved = DeleteActiveModulePermitUnderLease();

        KillSwitchProbe finalProbe = Probe();
        if (finalProbe.State != KillSwitchState.Armed)
        {
            throw new IOException(
                $"The emergency flag couldn't be confirmed after arming: {finalProbe.Error ?? finalProbe.State.ToString()}.");
        }

        return new KillSwitchResult(
            flagChanged ? "armed" : "already_armed",
            FlagPath,
            finalProbe.State,
            flagChanged,
            ActiveModulePath,
            permitRemoved,
            DateTimeOffset.UtcNow);
    }

    public static ModuleActivationResult ActivateModuleUnderLease(
        StateGateLease lease,
        string moduleId)
    {
        EnsureLeaseHeld(lease);
        if (!IsAllowedModuleId(moduleId))
        {
            throw new ArgumentException(
                $"Module id isn't allowlisted: {moduleId}",
                nameof(moduleId));
        }

        KillSwitchProbe initialProbe = Probe();
        if (initialProbe.State != KillSwitchState.Armed)
        {
            throw new InvalidOperationException(
                initialProbe.State == KillSwitchState.Unknown
                    ? $"The emergency flag state is unknown; activation is blocked: {initialProbe.Error}"
                    : "The emergency flag must be armed before issuing a module permit.");
        }

        ActiveModuleProbe initialPermit = ProbeActiveModule();
        if (initialPermit.State != ActiveModuleState.Absent)
        {
            throw new InvalidOperationException(
                initialPermit.State == ActiveModuleState.Unknown
                    ? $"The active-module permit state is unknown; activation is blocked: {initialPermit.Error}"
                    : "An active-module permit already exists. Run arm-kill-switch to revoke it first.");
        }

        bool activationCommitted = false;
        try
        {
            // The permit is exact ASCII with no BOM or newline so native modules can
            // compare it byte-for-byte while holding the same named state gate.
            WriteFileAtomically(
                ActiveModulePath,
                Encoding.ASCII.GetBytes(moduleId),
                overwrite: false);

            ActiveModuleProbe permitProbe = ProbeActiveModule();
            if (permitProbe.State != ActiveModuleState.Valid ||
                !string.Equals(permitProbe.ModuleId, moduleId, StringComparison.Ordinal))
            {
                throw new IOException(
                    $"The active-module permit couldn't be verified: {permitProbe.Error ?? permitProbe.State.ToString()}.");
            }

            // Deny delete while making the required second armed-state check. The
            // handle is released only for the intentional flag deletion below.
            using (KillSwitchGuard guard = OpenArmedGuardUnderLease(lease))
            {
                KillSwitchProbe armedAgain = Probe();
                if (armedAgain.State != KillSwitchState.Armed)
                {
                    throw new IOException(
                        $"The emergency flag changed while preparing activation: {armedAgain.Error ?? armedAgain.State.ToString()}.");
                }

                ActiveModuleProbe permitAgain = ProbeActiveModule();
                if (permitAgain.State != ActiveModuleState.Valid ||
                    !string.Equals(
                        permitAgain.ModuleId,
                        moduleId,
                        StringComparison.Ordinal))
                {
                    throw new IOException(
                        $"The active-module permit changed while preparing activation: {permitAgain.Error ?? permitAgain.State.ToString()}.");
                }
            }

            File.Delete(FlagPath);
            KillSwitchProbe finalProbe = Probe();
            if (finalProbe.State != KillSwitchState.Disarmed)
            {
                throw new IOException(
                    $"The emergency flag wasn't confirmed disarmed: {finalProbe.Error ?? finalProbe.State.ToString()}.");
            }

            activationCommitted = true;
            return new ModuleActivationResult(
                "module_permitted",
                moduleId,
                ActiveModulePath,
                FlagPath,
                finalProbe.State,
                DateTimeOffset.UtcNow,
                permitProbe.ExpiresAtUtc ??
                    DateTimeOffset.UtcNow + ActivationPermitLifetime);
        }
        catch (Exception activationException)
        {
            if (!activationCommitted)
            {
                Exception? rollbackException = TryRestoreLockedStateUnderLease(lease);
                if (rollbackException is not null)
                {
                    throw new AggregateException(
                        "Activation failed and the locked state couldn't be fully restored.",
                        activationException,
                        rollbackException);
                }
            }

            throw;
        }
    }

    public static KillSwitchGuard OpenArmedGuardUnderLease(StateGateLease lease)
    {
        EnsureLeaseHeld(lease);
        KillSwitchProbe beforeOpen = Probe();
        if (beforeOpen.State != KillSwitchState.Armed)
        {
            throw new InvalidOperationException(
                beforeOpen.State == KillSwitchState.Unknown
                    ? $"The emergency flag state is unknown: {beforeOpen.Error}"
                    : "The emergency flag isn't armed.");
        }

        FileStream stream = new(
            FlagPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        try
        {
            KillSwitchProbe afterOpen = Probe();
            if (afterOpen.State != KillSwitchState.Armed)
            {
                throw new IOException(
                    $"The emergency flag changed while its recovery guard was opened: {afterOpen.Error ?? afterOpen.State.ToString()}.");
            }

            return new KillSwitchGuard(stream, FlagPath);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static bool EnsureFlagArmedUnderLease()
    {
        KillSwitchProbe initialProbe = Probe();
        if (initialProbe.State == KillSwitchState.Unknown)
        {
            throw new IOException(
                $"The emergency flag state is unknown; refusing to guess: {initialProbe.Error}");
        }

        bool changed = false;
        if (initialProbe.State == KillSwitchState.Disarmed)
        {
            var payload = new
            {
                schemaVersion = 1,
                state = "disabled",
                armedAtUtc = DateTimeOffset.UtcNow,
                reason = "manual-safety-interlock",
            };
            byte[] bytes = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(
                    payload,
                    new JsonSerializerOptions { WriteIndented = true }) +
                Environment.NewLine);

            try
            {
                WriteFileAtomically(FlagPath, bytes, overwrite: false);
                changed = true;
            }
            catch (IOException) when (Probe().State == KillSwitchState.Armed)
            {
                // A manual recovery command created the same safety flag first.
            }
        }

        KillSwitchProbe finalProbe = Probe();
        if (finalProbe.State != KillSwitchState.Armed)
        {
            throw new IOException(
                $"The emergency flag wasn't present after the atomic write: {finalProbe.Error ?? finalProbe.State.ToString()}.");
        }

        return changed;
    }

    private static bool DeleteActiveModulePermitUnderLease()
    {
        ActiveModuleProbe beforeDelete = ProbeActiveModule();
        if (beforeDelete.State == ActiveModuleState.Unknown)
        {
            throw new IOException(
                $"The active-module permit state is unknown: {beforeDelete.Error}");
        }

        bool existed = beforeDelete.State != ActiveModuleState.Absent;
        File.Delete(ActiveModulePath);

        ActiveModuleProbe afterDelete = ProbeActiveModule();
        if (afterDelete.State != ActiveModuleState.Absent)
        {
            throw new IOException(
                $"The active-module permit wasn't removed: {afterDelete.Error ?? afterDelete.State.ToString()}.");
        }

        return existed;
    }

    private static Exception? TryRestoreLockedStateUnderLease(
        StateGateLease lease)
    {
        List<Exception> failures = [];
        KillSwitchGuard? guard = null;
        try
        {
            _ = EnsureFlagArmedUnderLease();
            guard = OpenArmedGuardUnderLease(lease);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            // Removing a permit is fail-safe even if the flag couldn't be
            // confirmed. If arming succeeded, the guard prevents a delete race.
            _ = DeleteActiveModulePermitUnderLease();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            guard?.Dispose();
        }

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(failures),
        };
    }

    private static FilePresenceProbe ProbePath(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return new FilePresenceProbe(SafetyFileState.Present, null);
        }
        catch (FileNotFoundException)
        {
            return new FilePresenceProbe(SafetyFileState.Absent, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new FilePresenceProbe(SafetyFileState.Absent, null);
        }
        catch (Exception exception) when (IsFileProbeException(exception))
        {
            return new FilePresenceProbe(
                SafetyFileState.Unknown,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsFileProbeException(Exception exception) =>
        exception is IOException or
        UnauthorizedAccessException or
        SecurityException or
        NotSupportedException or
        ArgumentException;

    private static void WriteFileAtomically(
        string destinationPath,
        byte[] bytes,
        bool overwrite)
    {
        Directory.CreateDirectory(StateDirectory);
        string temporaryPath = Path.Combine(
            StateDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destinationPath, overwrite);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (IsFileProbeException(exception))
            {
                // Cleanup can't change the destination's committed state. Leave an
                // orphaned, uniquely named temp file for later manual inspection.
            }
        }
    }

    private static void EnsureLeaseHeld(StateGateLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.IsHeld)
        {
            throw new InvalidOperationException("The JARVIS2 state gate isn't held.");
        }
    }

    private static string ResolveStateDirectory()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "Windows did not provide a LocalApplicationData directory.");
        }

        return Path.Combine(localApplicationData, "JARVIS2");
    }
}

internal sealed class StateGateLease : IDisposable
{
    private Semaphore? semaphore;

    private StateGateLease(Semaphore semaphore)
    {
        this.semaphore = semaphore;
    }

    public bool IsHeld => semaphore is not null;

    public static StateGateLease Acquire(string name, TimeSpan timeout)
    {
        Semaphore semaphore = new(1, 1, name, out _);
        try
        {
            if (!semaphore.WaitOne(timeout))
            {
                throw new TimeoutException(
                    $"Timed out waiting for the JARVIS2 state gate {name}.");
            }

            return new StateGateLease(semaphore);
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Semaphore? held = Interlocked.Exchange(ref semaphore, null);
        if (held is null)
        {
            return;
        }

        try
        {
            _ = held.Release();
        }
        finally
        {
            held.Dispose();
        }
    }
}

internal sealed class KillSwitchGuard : IDisposable
{
    private FileStream? stream;

    internal KillSwitchGuard(FileStream stream, string path)
    {
        this.stream = stream;
        Path = path;
    }

    public string Path { get; }

    public bool IsHeld => stream is not null;

    public void Dispose() => Interlocked.Exchange(ref stream, null)?.Dispose();
}

internal enum KillSwitchState
{
    Armed,
    Disarmed,
    Unknown,
}

internal enum ActiveModuleState
{
    Absent,
    Valid,
    Invalid,
    Unknown,
}

internal enum SafetyFileState
{
    Present,
    Absent,
    Unknown,
}

internal sealed record KillSwitchProbe(
    KillSwitchState State,
    string Path,
    string? Error)
{
    public bool BlocksInitialization => State != KillSwitchState.Disarmed;
}

internal sealed record ActiveModuleProbe(
    ActiveModuleState State,
    string Path,
    string? ModuleId,
    DateTimeOffset? LastWriteTimeUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? Error);

internal sealed record FilePresenceProbe(
    SafetyFileState State,
    string? Error);

internal sealed record KillSwitchResult(
    string Status,
    string Path,
    KillSwitchState State,
    bool Changed,
    string ActiveModulePath,
    bool ActiveModulePermitRemoved,
    DateTimeOffset ObservedAtUtc);

internal sealed record ModuleActivationResult(
    string Status,
    string ModuleId,
    string ActiveModulePath,
    string KillSwitchPath,
    KillSwitchState KillSwitchState,
    DateTimeOffset ActivatedAtUtc,
    DateTimeOffset PermitExpiresAtUtc);
