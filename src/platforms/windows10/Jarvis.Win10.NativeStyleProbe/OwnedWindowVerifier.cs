using System.Windows;

namespace Jarvis.Win10.NativeStyleProbe;

internal static class OwnedWindowVerifier
{
    public static OwnedWindowVerificationReceipt Verify(
        HostProbeReceipt hostReceipt)
    {
        if (!hostReceipt.Passed ||
            string.IsNullOrWhiteSpace(hostReceipt.MatchedProfileId))
        {
            throw new InvalidOperationException(
                "Exact Win10 host admission is required before creating the owned verification HWND.");
        }

        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
        Application application = new()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        Window ownedWindow = new()
        {
            Title = "JARVIS V2 Win10 owned HWND verifier",
            Width = 320,
            Height = 180,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow,
        };

        try
        {
            ownedWindow.Show();
            OwnedWindowStyleResult dark =
                OwnedWindowStyler.Apply(
                    ownedWindow,
                    NativeStylePreset.JarvisGraphite);
            OwnedWindowStyleResult reset =
                OwnedWindowStyler.Apply(
                    ownedWindow,
                    NativeStylePreset.SystemDefault);
            IReadOnlyList<DwmStyleCall> calls =
                [.. dark.Calls, .. reset.Calls];
            bool passed = calls.All(call => call.HResult >= 0);

            return new OwnedWindowVerificationReceipt(
                1,
                "jarvisv2-win10-owned-window-style-verification",
                passed ? "passed-own-window-only" : "failed",
                observedAtUtc,
                hostReceipt.MatchedProfileId,
                Environment.ProcessId,
                ToHex(dark.WindowHandle),
                calls,
                "own-process-hwnd-only",
                true,
                false,
                false,
                false,
                "not-run",
                passed
                    ? null
                    : "One or more DWM calls rejected the reviewed Win10 attribute.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.Runtime.InteropServices.ExternalException)
        {
            return new OwnedWindowVerificationReceipt(
                1,
                "jarvisv2-win10-owned-window-style-verification",
                "failed",
                observedAtUtc,
                hostReceipt.MatchedProfileId,
                Environment.ProcessId,
                "0x0",
                [],
                "own-process-hwnd-only",
                false,
                false,
                false,
                false,
                "not-run",
                $"{exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            ownedWindow.Close();
            application.Shutdown();
        }
    }

    private static string ToHex(nint windowHandle) =>
        $"0x{unchecked((ulong)windowHandle.ToInt64()):X}";
}
