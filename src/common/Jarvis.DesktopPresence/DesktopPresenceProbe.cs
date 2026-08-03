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
}
