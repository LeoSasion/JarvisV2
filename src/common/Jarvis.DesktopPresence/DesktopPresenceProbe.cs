using System.Drawing;

namespace Jarvis.DesktopPresence;

public sealed record DesktopPresenceProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    bool RegistrationEnablePassed,
    bool RegistrationIdempotencePassed,
    bool RegistrationDriftVisible,
    bool RegistrationDisablePassed,
    bool ExactResumeCommandPassed,
    bool SingleInstanceAdmissionPassed,
    bool SecondaryActivationPassed,
    bool PrimaryReacquirePassed,
    bool SummonHotKeyContractPassed,
    bool SummonHotKeyConflictVisible,
    bool SummonHotKeyReleased,
    bool AttentionIconsPassed,
    bool ProductionStartupStateTouched,
    IReadOnlyList<string> Failures);

public static class DesktopPresenceProbe
{
    public static async Task<DesktopPresenceProbeReceipt> RunAsync()
    {
        List<string> failures = [];
        string root = Path.Combine(
            Path.GetTempPath(),
            "jarvis2-desktop-presence-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        bool registrationEnablePassed = false;
        bool registrationIdempotencePassed = false;
        bool registrationDriftVisible = false;
        bool registrationDisablePassed = false;
        bool exactResumeCommandPassed = false;
        bool singleInstanceAdmissionPassed = false;
        bool secondaryActivationPassed = false;
        bool primaryReacquirePassed = false;
        bool summonHotKeyContractPassed = false;
        bool summonHotKeyConflictVisible = false;
        bool summonHotKeyReleased = false;
        bool attentionIconsPassed = false;
        try
        {
            string firstExecutable = Path.Combine(root, "jarvis-control-center.exe");
            string secondExecutable = Path.Combine(root, "jarvis-control-center-next.exe");
            await File.WriteAllBytesAsync(
                firstExecutable,
                [(byte)'M', (byte)'Z', 0, 0]);
            await File.WriteAllBytesAsync(
                secondExecutable,
                [(byte)'M', (byte)'Z', 0, 0]);

            MemoryStartupValueStore store = new();
            DesktopStartupRegistration registration = new(store);
            DesktopStartupRegistrationReceipt enabled =
                registration.SetEnabled(firstExecutable, enabled: true);
            registrationEnablePassed =
                enabled.Result == "passed" &&
                enabled.Enabled &&
                enabled.CurrentExecutable &&
                enabled.Changed &&
                store.WriteCount == 1;
            DesktopStartupRegistrationReceipt repeated =
                registration.SetEnabled(firstExecutable, enabled: true);
            registrationIdempotencePassed =
                repeated.Enabled &&
                repeated.CurrentExecutable &&
                !repeated.Changed &&
                store.WriteCount == 1;
            DesktopStartupRegistrationReceipt drifted =
                registration.Inspect(secondExecutable);
            registrationDriftVisible =
                drifted.State ==
                    DesktopStartupRegistrationState.EnabledForDifferentExecutable &&
                drifted.Enabled &&
                !drifted.CurrentExecutable;
            exactResumeCommandPassed = string.Equals(
                enabled.ExpectedCommand,
                $"\"{Path.GetFullPath(firstExecutable)}\" --resume-latest --minimized",
                StringComparison.Ordinal);
            DesktopStartupRegistrationReceipt disabled =
                registration.SetEnabled(secondExecutable, enabled: false);
            registrationDisablePassed =
                !disabled.Enabled &&
                disabled.Changed &&
                store.DeleteCount == 1 &&
                store.Read() is null;

            string scope = "probe-" + Guid.NewGuid().ToString("N");
            using ManualResetEventSlim activated = new(false);
            using (ControlCenterSingleInstance primary =
                   ControlCenterSingleInstance.AcquireForScope(scope))
            using (ControlCenterSingleInstance secondary =
                   ControlCenterSingleInstance.AcquireForScope(scope))
            {
                singleInstanceAdmissionPassed =
                    primary.IsPrimary && !secondary.IsPrimary;
                primary.StartListening(() => activated.Set());
                secondaryActivationPassed =
                    secondary.SignalPrimary() &&
                    activated.Wait(TimeSpan.FromSeconds(3));
            }
            using ControlCenterSingleInstance reacquired =
                ControlCenterSingleInstance.AcquireForScope(scope);
            primaryReacquirePassed = reacquired.IsPrimary;

            MemoryDesktopHotKeyNative hotKeyNative = new();
            using (DesktopSummonHotKey hotKey = new(hotKeyNative))
            {
                DesktopSummonHotKeyReceipt registered = hotKey.Register(42);
                summonHotKeyContractPassed =
                    registered.Result == "passed" &&
                    registered.Registered &&
                    registered.Chord == DesktopSummonHotKey.Chord &&
                    hotKeyNative.WindowHandle == 42 &&
                    hotKeyNative.Identifier != 0 &&
                    hotKeyNative.Modifiers == 0x4003 &&
                    hotKeyNative.VirtualKey == 0x4A &&
                    DesktopSummonHotKey.IsSummonMessage(
                        DesktopSummonHotKey.WindowsMessageId,
                        hotKeyNative.Identifier);
            }
            summonHotKeyReleased = hotKeyNative.UnregisterCount == 1;

            MemoryDesktopHotKeyNative conflictNative = new()
            {
                RegisterResult = false,
                LastError = 1409,
            };
            using DesktopSummonHotKey conflicting = new(conflictNative);
            DesktopSummonHotKeyReceipt conflict = conflicting.Register(84);
            summonHotKeyConflictVisible =
                conflict.Result == "unavailable" &&
                !conflict.Registered &&
                conflict.ConflictVisible &&
                conflict.NativeError == 1409 &&
                conflict.Failures.Contains(
                    "summon-chord-already-registered",
                    StringComparer.Ordinal);

            using Icon readyIcon = JarvisPresenceIcon.Create(
                JarvisPresenceSignal.Ready);
            using Icon workingIcon = JarvisPresenceIcon.Create(
                JarvisPresenceSignal.Working);
            using Icon ownerIcon = JarvisPresenceIcon.Create(
                JarvisPresenceSignal.OwnerActionRequired);
            using Icon faultIcon = JarvisPresenceIcon.Create(
                JarvisPresenceSignal.Faulted);
            attentionIconsPassed =
                readyIcon.Size == new Size(32, 32) &&
                workingIcon.Size == new Size(32, 32) &&
                ownerIcon.Size == new Size(32, 32) &&
                faultIcon.Size == new Size(32, 32) &&
                readyIcon.Handle != nint.Zero &&
                workingIcon.Handle != nint.Zero &&
                ownerIcon.Handle != nint.Zero &&
                faultIcon.Handle != nint.Zero;
        }
        catch (Exception exception)
        {
            failures.Add(exception.GetType().Name + ": " + exception.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception exception)
            {
                failures.Add(
                    "probe-cleanup: " + exception.GetType().Name + ": " +
                    exception.Message);
            }
        }

        if (!registrationEnablePassed)
        {
            failures.Add("registration-enable");
        }
        if (!registrationIdempotencePassed)
        {
            failures.Add("registration-idempotence");
        }
        if (!registrationDriftVisible)
        {
            failures.Add("registration-drift");
        }
        if (!registrationDisablePassed)
        {
            failures.Add("registration-disable");
        }
        if (!exactResumeCommandPassed)
        {
            failures.Add("exact-resume-command");
        }
        if (!singleInstanceAdmissionPassed)
        {
            failures.Add("single-instance-admission");
        }
        if (!secondaryActivationPassed)
        {
            failures.Add("secondary-activation");
        }
        if (!primaryReacquirePassed)
        {
            failures.Add("primary-reacquire");
        }
        if (!summonHotKeyContractPassed)
        {
            failures.Add("summon-hot-key-contract");
        }
        if (!summonHotKeyConflictVisible)
        {
            failures.Add("summon-hot-key-conflict");
        }
        if (!summonHotKeyReleased)
        {
            failures.Add("summon-hot-key-release");
        }
        if (!attentionIconsPassed)
        {
            failures.Add("attention-icons");
        }

        return new DesktopPresenceProbeReceipt(
            1,
            "jarvisv2-desktop-presence-probe",
            failures.Count == 0 ? "passed" : "failed",
            registrationEnablePassed,
            registrationIdempotencePassed,
            registrationDriftVisible,
            registrationDisablePassed,
            exactResumeCommandPassed,
            singleInstanceAdmissionPassed,
            secondaryActivationPassed,
            primaryReacquirePassed,
            summonHotKeyContractPassed,
            summonHotKeyConflictVisible,
            summonHotKeyReleased,
            attentionIconsPassed,
            ProductionStartupStateTouched: false,
            failures);
    }

    private sealed class MemoryStartupValueStore :
        DesktopStartupRegistration.IDesktopStartupValueStore
    {
        private string? value;

        public int WriteCount { get; private set; }

        public int DeleteCount { get; private set; }

        public string? Read() => value;

        public void Write(string command)
        {
            value = command;
            WriteCount++;
        }

        public void Delete()
        {
            value = null;
            DeleteCount++;
        }
    }

    private sealed class MemoryDesktopHotKeyNative :
        DesktopSummonHotKey.IDesktopHotKeyNative
    {
        public bool RegisterResult { get; init; } = true;

        public int LastError { get; init; }

        public nint WindowHandle { get; private set; }

        public nint Identifier { get; private set; }

        public uint Modifiers { get; private set; }

        public uint VirtualKey { get; private set; }

        public int UnregisterCount { get; private set; }

        public bool RegisterHotKey(
            nint windowHandle,
            int identifier,
            uint modifiers,
            uint virtualKey)
        {
            WindowHandle = windowHandle;
            Identifier = identifier;
            Modifiers = modifiers;
            VirtualKey = virtualKey;
            return RegisterResult;
        }

        public bool UnregisterHotKey(nint windowHandle, int identifier)
        {
            if (windowHandle == WindowHandle && identifier == Identifier)
            {
                UnregisterCount++;
                return true;
            }
            return false;
        }

        public int GetLastError() => LastError;
    }
}
