# Pi Agent desktop runtime ownership

`PiAgentDesktopRuntime` is the non-visual composition root for an owned JARVIS
desktop conversation. It is the boundary a future WPF application will start
after it has selected an admitted workspace and constructed a reviewed
`IDesktopModelProvider`.

## Owned resources

One runtime instance owns, in order:

1. one current-user `DesktopModelBrokerServer`;
2. one no-shell Node sidecar configured with that broker pipe;
3. one admitted, root-confined, in-memory Pi SDK session;
4. one `PiAgentConversationState`.

The caller supplies no pipe name. `PiAgentDesktopRuntime.StartAsync` creates the
broker first, injects only its generated pipe capability into the sidecar, and
admits the returned session receipt before publishing the runtime. The receipt
must prove the exact four read-only tools, the desktop broker model identity,
in-memory session state, disabled resource discovery and disabled sidecar model
network.

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
4. exports completed turns and commits the encrypted checkpoint, when a store
   is configured;
5. requests orderly sidecar shutdown.

`DisposeAsync` then disposes the owned sidecar and broker. If orderly shutdown
does not complete within the reviewed timeout, existing controller cleanup is
limited to the Node process tree that this runtime started.

Quiescing is visible through the existing immutable conversation snapshot:
`CanSubmit` remains false after shutdown, even when no turn is active. This
prevents a closing WPF window from racing a new prompt against sidecar disposal.

## Current boundary

The runtime accepts a provider-neutral `IDesktopModelProvider`; it does not
select a production provider, read credentials or create a credential store.
The Pi sidecar remains offline and has only `read`, `grep`, `find` and `ls`
inside one admitted workspace. Pi SDK session persistence remains disabled.
The desktop can persist only the bounded completed-text checkpoint in its
CurrentUser-DPAPI store; the sidecar never reads the store or encryption key.
The runtime does not enable mutation tools, contact Explorer, modify the
registry or control physical RGB devices.

The deterministic `runtime-probe` command proves:

- three turns through the composed runtime, including a real read-tool round
  trip;
- export of three completed text turns, followed by a fresh sidecar restoring
  all six context messages before a continuation prompt;
- CurrentUser-DPAPI encrypted save/load, automatic store-backed restore,
  workspace-copy rejection and ciphertext-corruption rejection;
- fail-closed rejection of duplicate and over-limit checkpoints;
- exact broker/session admission;
- rejection of submissions after quiesce;
- active-turn cancellation during shutdown;
- cleanup of the provider, broker and owned sidecar after a protected-root
  startup rejection;
- zero broker faults and credential-free, diagnostic-only model traffic.

Run it as part of `scripts/Test-PiAgentHost.ps1`. The envelope and lifecycle
details are in `PI-AGENT-DESKTOP-CHECKPOINT-STORE.md`.
