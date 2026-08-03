using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Jarvis.DesktopPresence;

public sealed record DesktopSummonHotKeyReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string Chord,
    bool Registered,
    bool ConflictVisible,
    int NativeError,
    IReadOnlyList<string> Failures);

public sealed class DesktopSummonHotKey : IDisposable
{
    public const int WindowsMessageId = 0x0312;
    public const string Chord = "Ctrl+Alt+J";

    private const int HotKeyId = 0x4A32;
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierNoRepeat = 0x4000;
    private const uint VirtualKeyJ = 0x4A;
    private const int ErrorHotKeyAlreadyRegistered = 1409;

    private readonly IDesktopHotKeyNative native;
    private nint registeredWindow;
    private int disposed;

    public DesktopSummonHotKey()
        : this(new User32DesktopHotKeyNative())
    {
    }

    internal DesktopSummonHotKey(IDesktopHotKeyNative native)
    {
        this.native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public bool Registered => registeredWindow != nint.Zero;

    public DesktopSummonHotKeyReceipt Register(nint windowHandle)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException(
                "A live owner window handle is required.",
                nameof(windowHandle));
        }
        if (registeredWindow != nint.Zero)
        {
            if (registeredWindow != windowHandle)
            {
                throw new InvalidOperationException(
                    "The summon hot key is already bound to another window.");
            }
            return CreateReceipt(
                result: "passed",
                registered: true,
                nativeError: 0,
                failures: []);
        }

        bool registered = native.RegisterHotKey(
            windowHandle,
            HotKeyId,
            ModifierAlt | ModifierControl | ModifierNoRepeat,
            VirtualKeyJ);
        if (registered)
        {
            registeredWindow = windowHandle;
            return CreateReceipt(
                result: "passed",
                registered: true,
                nativeError: 0,
                failures: []);
        }

        int nativeError = native.GetLastError();
        string failure = nativeError == ErrorHotKeyAlreadyRegistered
            ? "summon-chord-already-registered"
            : "register-hot-key-failed: " +
              new Win32Exception(nativeError).Message;
        return CreateReceipt(
            result: "unavailable",
            registered: false,
            nativeError,
            failures: [failure]);
    }

    public static bool IsSummonMessage(int message, nint wParam) =>
        message == WindowsMessageId && wParam == HotKeyId;

    public static bool TrySetForegroundWindow(nint windowHandle) =>
        windowHandle != nint.Zero &&
        NativeMethods.SetForegroundWindow(windowHandle);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        if (registeredWindow != nint.Zero)
        {
            _ = native.UnregisterHotKey(registeredWindow, HotKeyId);
            registeredWindow = nint.Zero;
        }
    }

    private static DesktopSummonHotKeyReceipt CreateReceipt(
        string result,
        bool registered,
        int nativeError,
        IReadOnlyList<string> failures) =>
        new(
            1,
            "jarvisv2-desktop-summon-hot-key-receipt",
            result,
            Chord,
            registered,
            !registered && nativeError == ErrorHotKeyAlreadyRegistered,
            nativeError,
            failures);

    internal interface IDesktopHotKeyNative
    {
        bool RegisterHotKey(
            nint windowHandle,
            int identifier,
            uint modifiers,
            uint virtualKey);

        bool UnregisterHotKey(nint windowHandle, int identifier);

        int GetLastError();
    }

    private sealed class User32DesktopHotKeyNative : IDesktopHotKeyNative
    {
        public bool RegisterHotKey(
            nint windowHandle,
            int identifier,
            uint modifiers,
            uint virtualKey) =>
            NativeMethods.RegisterHotKey(
                windowHandle,
                identifier,
                modifiers,
                virtualKey);

        public bool UnregisterHotKey(nint windowHandle, int identifier) =>
            NativeMethods.UnregisterHotKey(windowHandle, identifier);

        public int GetLastError() => Marshal.GetLastWin32Error();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(
            nint windowHandle,
            int identifier,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(
            nint windowHandle,
            int identifier);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(nint windowHandle);
    }
}
