# Pi Agent desktop conversation state

`PiAgentConversationState` is the non-visual desktop adapter between the
ordered Pi Agent turn stream and a future conversation surface. It lives in
`Jarvis.PiAgentHost`; `Jarvis.ControlCenter` references that assembly and
exposes an `INotifyPropertyChanged` binding wrapper without changing its XAML.

## State model

The adapter publishes immutable `PiAgentConversationSnapshot` values. Every
snapshot contains:

- a monotonically increasing revision;
- the active turn ID, if any;
- `CanSubmit` and `CanCancel` command state;
- retained user/assistant turns;
- ordered tool calls and their running, completed or failed state;
- the latest admitted event sequence and terminal error code.

One conversation admits one active generation at a time. This preserves Pi
session ordering while still allowing the underlying desktop response pump to
process cancellation and broker traffic concurrently.

```text
Starting -> Running -> Completed
                    -> Aborted
                    -> Failed
```

Cancellation first publishes `CancelRequested`, disables the cancel command,
then waits for the Pi terminal event. A failed abort request restores the
cancel command unless the turn already became terminal.

`QuiesceAsync` permanently closes submission admission, publishes the changed
snapshot, requests cancellation of an active turn and waits for its terminal
event. `PiAgentDesktopRuntime` uses this before sidecar shutdown so window
closing cannot race a new prompt against transport disposal.

## UI dispatch

The adapter accepts a captured `SynchronizationContext`. Stream events may
arrive on the managed output-pump thread, but snapshot notifications are posted
to that context. A WPF host can therefore construct the adapter on its
dispatcher thread and bind without giving the UI ownership of the sidecar
process or event channel.

`PiAgentConversationBinding` converts snapshots into
`INotifyPropertyChanged` updates for `Snapshot`, `ActiveTurnId`, `CanSubmit`,
`CanCancel` and `Turns`. It is compiled into `Jarvis.ControlCenter`, but no
conversation panel is visible yet.

## Bounded retention

The presentation layer retains at most 128 terminal turns and 262,144
assistant characters per turn. Old terminal turns are removed before a new
turn is admitted. The single active turn is never evicted.

`ExportCheckpoint` creates a separate resume boundary from that presentation
retention. It selects a newest-first contiguous suffix of completed turns, then
returns them in conversational order. A checkpoint is limited to 32
user/assistant pairs, 32,768 serialized UTF-8 bytes and 16,384 UTF-8 bytes per
text field. Failed, aborted, active and tool-event payloads are never exported.
The turn IDs and final plain text are retained so restored UI state and restored
Pi model context describe the same conversation.

The adapter also rejects:

- empty or over-limit input;
- malformed or duplicate turn IDs;
- concurrent active turns;
- duplicate or unmatched tool IDs;
- skipped, repeated or cross-turn event sequences;
- terminal responses that diverge from streamed text;
- terminal events while a tool is still running.

## Current boundary

The diagnostic path proves three completed turns, incremental text snapshots,
a real root-confined `read` tool lifecycle, checkpoint export and context
restore into a fresh Pi SDK session, single-active-turn rejection and a
separately aborted turn. It uses the deterministic offline desktop broker:
credentials are not transported, the Pi sidecar has no model network, and
Explorer is not touched.

This milestone changes no visible layout. Before a WPF conversation panel or
effect-bearing interaction is implemented, four image proposals must be
reviewed with the user.
