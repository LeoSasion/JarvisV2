using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jarvis.PiAgentHost;

namespace Jarvis.ControlCenter;

public sealed class PiAgentConversationBinding :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly PiAgentConversationState conversation;
    private PiAgentConversationSnapshot snapshot;
    private bool disposed;

    public PiAgentConversationBinding(
        PiAgentConversationState conversation)
    {
        this.conversation = conversation ??
            throw new ArgumentNullException(nameof(conversation));
        snapshot = conversation.Snapshot;
        conversation.SnapshotChanged += OnSnapshotChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PiAgentConversationSnapshot Snapshot => snapshot;
    public string? ActiveTurnId => snapshot.ActiveTurnId;
    public bool CanSubmit => snapshot.CanSubmit;
    public bool CanCancel => snapshot.CanCancel;
    public IReadOnlyList<PiAgentConversationTurnSnapshot> Turns =>
        snapshot.Turns;

    public async Task<PiAgentConversationTurn> SubmitAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return await conversation.SubmitAsync(
            text,
            cancellationToken: cancellationToken);
    }

    public async Task<bool> CancelAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return await conversation.CancelActiveTurnAsync(
            cancellationToken);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        conversation.SnapshotChanged -= OnSnapshotChanged;
    }

    private void OnSnapshotChanged(
        object? sender,
        PiAgentConversationSnapshotChangedEventArgs eventArgs)
    {
        snapshot = eventArgs.Snapshot;
        RaisePropertyChanged(nameof(Snapshot));
        RaisePropertyChanged(nameof(ActiveTurnId));
        RaisePropertyChanged(nameof(CanSubmit));
        RaisePropertyChanged(nameof(CanCancel));
        RaisePropertyChanged(nameof(Turns));
    }

    private void RaisePropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
