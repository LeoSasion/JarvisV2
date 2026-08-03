using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Jarvis.DesktopPresence;

public sealed class ControlCenterSingleInstance : IDisposable
{
    private const string NamePrefix = @"Local\JARVIS2.ControlCenter.";

    private readonly EventWaitHandle activationEvent;
    private RegisteredWaitHandle? listener;
    private int disposed;

    private ControlCenterSingleInstance(
        EventWaitHandle activationEvent,
        bool isPrimary)
    {
        this.activationEvent = activationEvent;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    public static ControlCenterSingleInstance Acquire() =>
        AcquireForScope(CreateCurrentUserScope());

    internal static ControlCenterSingleInstance AcquireForScope(string scope)
    {
        if (
            string.IsNullOrWhiteSpace(scope) ||
            scope.Length > 256 ||
            scope.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException(
                "The single-instance scope is invalid.",
                nameof(scope));
        }
        string name = NamePrefix + HashScope(scope);
        EventWaitHandle activationEvent = new(
            initialState: false,
            EventResetMode.AutoReset,
            name,
            out bool createdNew);
        return new ControlCenterSingleInstance(activationEvent, createdNew);
    }

    public void StartListening(Action activationRequested)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(activationRequested);
        if (!IsPrimary)
        {
            throw new InvalidOperationException(
                "Only the primary Control Center instance can listen.");
        }
        if (listener is not null)
        {
            throw new InvalidOperationException(
                "The activation listener is already registered.");
        }
        listener = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            static (state, timedOut) =>
            {
                if (timedOut || state is not Action activation)
                {
                    return;
                }
                try
                {
                    activation();
                }
                catch
                {
                    // A foreground request must never terminate the owner process.
                }
            },
            activationRequested,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);
    }

    public bool SignalPrimary()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        if (IsPrimary)
        {
            return false;
        }
        return activationEvent.Set();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        listener?.Unregister(waitObject: null);
        listener = null;
        activationEvent.Dispose();
    }

    private static string CreateCurrentUserScope()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ??
            throw new InvalidOperationException(
                "The current Windows user has no security identifier.");
        return $"user-{sid.Replace('-', 'x')}";
    }

    private static string HashScope(string scope)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(scope));
        return Convert.ToHexString(hash[..16]).ToLowerInvariant();
    }
}
