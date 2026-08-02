# Pi Agent desktop runtime ownership

`PiAgentDesktopRuntime` is the non-visual composition root for an owned JARVIS
desktop conversation. `Jarvis.ControlCenter` starts this boundary after its
portable/developer bootstrap resolves Node and the sidecar for one admitted
workspace. It then constructs either the default local diagnostic provider or
the explicitly selected OpenAI Responses provider.

## Owned resources

One runtime instance owns, in order:

1. one current-user `DesktopModelBrokerServer`;
2. one no-shell Node sidecar configured with that broker pipe;
3. one admitted, root-confined, in-memory Pi SDK session;
4. one `PiAgentConversationState`.

The caller supplies no pipe name. `PiAgentDesktopRuntime.StartAsync` creates the
broker first, injects only its generated pipe capability into the sidecar, and
admits the returned session receipt before publishing the runtime. The receipt
  must prove the exact four read tools plus non-mutating `propose_edit`,
  `propose_patch` and `propose_create_file`, the
desktop broker model identity,
in-memory session state, disabled resource discovery and disabled sidecar model
network.

Sidecar readiness uses a fail-closed adapter anchored to the exact pinned Pi
package entry. It imports only the reviewed core modules used by the desktop
session instead of evaluating the package's full CLI, TUI and media export
graph.

The runtime may receive one explicit desktop-owned conversation checkpoint or
a `PiAgentConversationCheckpointStore`. Before the sidecar starts, managed code
loads the CurrentUser-DPAPI envelope when needed and admits its schema, exact
text limits, unique turn IDs and serialized UTF-8 size. The sidecar repeats
those checks, appends the admitted user/assistant message pairs to Pi's
in-memory `SessionManager`, and only then creates the Agent session. The session
receipt reports both the restored turn count and restored context-message
count, so a new desktop process resumes real model context rather than only
reconstructing visible chat rows.

If any startup stage fails, the owned Node process and broker are disposed
before the failure is returned. Once broker startup succeeds, provider
ownership transfers to the runtime and is released with the broker.

## Shutdown order

`ShutdownAsync` is idempotent and serializes concurrent shutdown requests. It:

1. quiesces the conversation so new turns are rejected;
2. requests cancellation of the active turn, if any;
3. waits for the terminal conversation event;
4. waits for ordered terminal autosaves and queues the final checkpoint when
   needed;
5. requests orderly sidecar shutdown.

`DisposeAsync` then disposes the owned sidecar and broker. If orderly shutdown
does not complete within the reviewed timeout, existing controller cleanup is
limited to the Node process tree that this runtime started.

Quiescing is visible through the existing immutable conversation snapshot:
`CanSubmit` remains false after shutdown, even when no turn is active. This
prevents a closing WPF window from racing a new prompt against sidecar disposal.

## Current boundary

The runtime remains provider-neutral: its WPF composition root selects the
provider and owns any provider credential source. Production mode uses the
desktop-only `OpenAiResponsesModelProvider`; its CurrentUser-DPAPI key never
enters the broker protocol or sidecar. The Pi sidecar remains offline and has
  only `read`, `grep`, `find`, `ls`, `propose_edit`, `propose_patch`,
  `propose_create_file` and `propose_change_set` inside one admitted
workspace. Pi SDK session persistence remains disabled.
The desktop can persist only the bounded completed-text checkpoint in its
CurrentUser-DPAPI store; the sidecar never reads the store or encryption key.
Each terminal turn is saved in order. A persistence failure closes further
  submissions and is surfaced during shutdown while sidecar cleanup still runs.
  Proposal tools do not write. Existing-file patches are limited to 2–8
  distinct, unique, non-overlapping hunks in one strict UTF-8 file and commit
  through the same single-file atomic replacement boundary. Change sets bind
  two to four ordered files to one review digest and one desktop decision; a
  strict journal converges interrupted work to all-before or
  all-committed-after state before tools return. Exact commit and discard requests remain
desktop-only and are exposed through conversation-state owner decisions. The
runtime does not enable shell or generic mutation tools, contact Explorer,
modify the registry or control physical RGB devices.

The current Control Center surface gives this shutdown path a 12-second
window-close budget and keeps the user-visible phase in `STOPPING` while the
runtime quiesces. It displays checkpoint and broker state but does not own or
inspect the sidecar process directly. See
`PI-AGENT-DESKTOP-CONVERSATION-SURFACE.md`.

The deterministic `runtime-probe` command proves:

- three turns through the composed runtime, including a real read-tool round
  trip;
- export of three completed text turns, followed by a fresh sidecar restoring
  all six context messages before a continuation prompt;
- CurrentUser-DPAPI encrypted save/load, automatic store-backed restore,
  workspace-copy rejection and ciphertext-corruption rejection;
- three ordered terminal autosaves, one resumed-turn autosave and fail-closed
  submission shutdown after a forced commit failure;
- fail-closed rejection of duplicate and over-limit checkpoints;
- exact broker/session admission;
- one structured existing-text proposal plus one mixed three-file change set,
  approved fixture commits, exact per-file receipts, replay rejection, drift
  rejection and explicit no-write rejection;
- rejection of submissions after quiesce;
- active-turn cancellation during shutdown;
- cleanup of the provider, broker and owned sidecar after a protected-root
  startup rejection;
- zero broker faults and credential-free, diagnostic-only model traffic.

Run it as part of `scripts/Test-PiAgentHost.ps1`. The envelope and lifecycle
details are in `PI-AGENT-DESKTOP-CHECKPOINT-STORE.md`. The separate offline
Responses protocol/credential probe is documented in
`PI-AGENT-OPENAI-RESPONSES-PROVIDER.md`, and portable discovery is documented
in `JARVIS-CONTROL-CENTER-PORTABLE-RUNTIME.md`.
The edit authority and commit sequence are documented in
`PI-AGENT-WORKSPACE-EDIT-APPROVAL.md`.
