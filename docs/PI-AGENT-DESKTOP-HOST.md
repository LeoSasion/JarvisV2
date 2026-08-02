# Pi Agent desktop host

JarvisV2 will embed the official
`@earendil-works/pi-coding-agent` runtime behind a separate Node.js sidecar.
The desktop host is a .NET/WPF process, so the first cross-language boundary
uses strict LF-delimited JSONL over standard input and output.

The Pi Agent and Pi AI dependencies are pinned exactly to `0.82.1`. Pi exposes
both an SDK for in-process JavaScript applications and RPC for
language-neutral clients. The sidecar resolves the pinned package entry and
loads only its reviewed core SDK modules through a fail-closed local adapter.
This keeps the package's CLI, TUI and media utility graph out of sidecar
readiness while keeping Node and model-provider state outside the Windows
Shell process.

## Current implemented slice

`Jarvis.PiAgentHost` now:

- imports and fingerprints the pinned Pi package;
- validates the pinned `dist/index.js` layout and loads only the SDK, model,
  session, settings, extension and tool-definition core modules needed by the
  desktop host;
- verifies the session, runtime and model-control exports needed by the future
  desktop host;
- serves a per-frame-bounded JSONL handshake and capability protocol;
- provides a managed desktop bridge that starts the exact Node sidecar without
  a shell, replaces the inherited environment with a minimal OS allowlist,
  admits the ready frame, probes capabilities and owns orderly shutdown;
- creates one real Pi SDK session bound to one explicit canonical workspace
  root;
- keeps the session in memory and disables project resource discovery,
  sidecar provider network access and credential storage;
- registers one custom Pi provider only when the desktop supplies a reviewed
  `\\.\pipe\jarvis2-pi-model-{guid}` endpoint;
- keeps provider credentials out of the sidecar and sends model context through
  a bounded, current-user named pipe owned by the desktop process;
- provides a provider-neutral `DesktopModelBrokerServer` that remains alive
  across multiple requests, admits at most four current-user connections and
  caps every broker frame at 1 MiB;
- validates provider event order and permits only the session's `read`, `grep`,
  `find`, `ls`, `propose_edit`, `propose_patch` and `propose_create_file` identities before forwarding
  tool calls to Pi;
- rejects an offline provider attempt to emit `bash`, returns a closed failure
  to the Pi turn and proves the broker remains isolated from the Shell;
- exposes conditional `start_turn` and `abort_turn` requests; assistant text,
  tool lifecycle and the terminal turn receipt stream independently of command
  responses;
- runs one managed output pump that demultiplexes concurrent responses and turn
  events, allowing the desktop to cancel generation while a prompt is active;
- exposes every accepted text delta, tool start, tool completion and terminal
  result through a 512-event bounded, ordered, single-consumer stream suitable
  for a future WPF conversation surface; if that consumer stops draining the
  buffer beyond the request deadline, the bridge fails the sidecar closed;
- folds that stream into immutable, revisioned desktop conversation snapshots
  with one active turn, bounded history, tool lifecycle state, cancellation
  command state and captured synchronization-context notification dispatch;
- composes the desktop-owned model broker, exact Node sidecar, admitted
  review-gated session and conversation state behind one
  `PiAgentDesktopRuntime`;
- exports only bounded completed text turns, restores those messages into a
  fresh Pi in-memory session and keeps the restored UI snapshot aligned with
  the model context;
- provides a workspace-bound Windows CurrentUser-DPAPI checkpoint store with a
  64 KiB envelope, reparse-point rejection and write-through atomic commit;
- serializes one encrypted save after each terminal turn; a persistence failure
  closes new submissions and is surfaced without abandoning sidecar cleanup;
- quiesces submissions, cancels any active turn, flushes queued checkpoint
  saves and shuts down the owned sidecar before disposing the broker;
- replaces the SDK file tools with root-confined `read`, `grep`, `find` and
  `ls` definitions plus `propose_edit` for exact existing-text replacement and
  `propose_patch` for 2–8 distinct, unique, non-overlapping exact replacements
  in one existing UTF-8 file, plus
  `propose_create_file` for a missing UTF-8 file beneath an existing parent;
  all proposal tools are non-mutating, while `bash`, generic `edit` and
  `write` stay unavailable;
- emits the proposal as a structured turn event, blocks new turns while one is
  pending, and exposes desktop-only commit and discard requests;
- binds approval to the exact proposal id, explicit operation and before-state
  SHA-256; replacement and patch recheck file identity/hash and reconstruct the
  exact reviewed result before one atomic replacement, while creation rechecks
  parent identity and target absence before exclusive no-overwrite creation;
  all reject replay and drift;
- lets the desktop owner arm one reviewed iteration only from a clean Git HEAD,
  with a fixed four-approved-edit limit and six-hour expiry;
- writes every policy and step transition to a workspace-bound CurrentUser
  DPAPI envelope, keeping completed workflow receipts under LocalAppData;
- automatically starts the next bounded reasoning turn only after the owner
  approved the prior exact edit and the fixed repository gate passed;
- pins HEAD, admits exactly the accumulated modified path set and after hashes,
  runs direct `git diff --check`, and non-executingly parses JSON plus
  XML/XAML/project files without running workspace code;
- suspends a live policy on shutdown, restores no proposal capability, and
  requires explicit re-arm plus repository revalidation after restart;
- rejects drive roots, protected OS/profile roots, relative paths, canonical
  aliases, junctions, symbolic links, workspace escapes and a second binding;
- rejects credential-shaped fields and frames over 64 KiB while accepting
  batched valid frames;
- reports whether any credential-shaped environment variable survived into the
  sidecar; the managed desktop bridge rejects readiness unless the result is
  clean;
- fault-injects a wrong ready protocol, an oversized ready frame and a hung
  startup; every case is rejected and cleanup is scoped to the owned Node
  process;
- fault-injects an invalid model pipe, wrong broker protocol, early disconnect
  and oversized broker response; each prompt fails closed;
- holds a model request open and proves both the Node host and managed desktop
  can abort the active Pi turn and observe `turn-aborted`;
- uses a deterministic `IDesktopModelProvider` to prove two ordinary turns,
  one real root-confined `read` tool round trip and active-turn cancellation
  through the production broker boundary without online model access;
- executes direct inside/outside file-tool probes and forces `PI_OFFLINE=1`
  before importing Pi.

This is now a real desktop-owned Pi conversation transport with a first native
product conversation surface in `Jarvis.ControlCenter`. With no broker pipe,
readiness and capabilities continue
to report `promptingEnabled: false`. With the reviewed pipe present, the bridge
can bind one review-gated workspace, run a real Pi prompt and receive incremental
assistant text across multiple turns. A turn runs in the background, so the
desktop can issue `abort_turn` without waiting for generation to finish. No
provider credential is inherited or transported, no resource is discovered
from the workspace and no session file is created. The model cannot approve its
own proposal; only the native desktop review controls can exercise the one-shot
decision.

The broker server and provider interface are production-facing boundaries; the
provider used by the product slice and audit is still deterministic and
offline. Connecting an authenticated production model provider and choosing
its credential store remain separate reviewed steps. The UI consumes the
conversation snapshots derived from `PiAgentTurnHandle.ReadEventsAsync()`;
the aggregate completion task remains available for non-streaming callers.
`PiAgentConversationState` owns that single event consumer for product UI use,
and `Jarvis.ControlCenter` presents it through an `INotifyPropertyChanged`
binding adapter and a visible handoff-rail surface. See
`PI-AGENT-DESKTOP-CONVERSATION-STATE.md` and
`PI-AGENT-DESKTOP-CONVERSATION-SURFACE.md`. The lifecycle composition root is
documented in `PI-AGENT-DESKTOP-RUNTIME.md`; encrypted persistence is documented
in `PI-AGENT-DESKTOP-CHECKPOINT-STORE.md`; the exact edit authority split is
documented in `PI-AGENT-WORKSPACE-EDIT-APPROVAL.md`.

## Prompting admission

Pi runs with the permissions of its host process and does not provide a
built-in operating-system permission sandbox. Jarvis therefore cannot treat
an authenticated agent session as equivalent to a UI widget. Workspace
admission remains independent from provider authentication. Prompting is
enabled only by a desktop-owned named-pipe capability; mutation tools and
unattended approval still require separate supervisor capabilities. The
reviewed-iteration coordinator can continue reasoning only inside its fixed
desktop-owner policy and only after the preceding one-shot approval plus
repository gate.

The planned progression is:

```text
WPF desktop
    |
    +-- managed sidecar lifecycle and bounded JSONL transport
            |
            +-- single-root review-gated Pi session admission (implemented)
                    |
                    +-- brokered streaming + active-turn abort (implemented)
                            |
                            +-- provider-neutral multi-request broker
                                (implemented)
                                    |
                                    +-- desktop-owned runtime lifecycle
                                        (implemented)
                                            |
                                            +-- encrypted checkpoint persistence
                                                (implemented)
                                            |
                                            +-- product conversation surface
                                                (local diagnostic provider implemented)
                                                    |
                                                    +-- authenticated production provider
                                                            |
                                                            +-- one-shot replace/patch/create owner approval
                                                                (implemented)
                                                                    |
                                                                    +-- durable reviewed self-iteration workflow
                                                                        (implemented)
```

No stage grants unattended approval, Shell injection, Explorer mutation,
registry writes or physical-device control.

## Validation

Install the locked dependency without lifecycle scripts, then run the full
audit:

```powershell
pnpm install --frozen-lockfile --ignore-scripts

pwsh -NoLogo -NoProfile -File `
  .\scripts\Test-PiAgentHost.ps1 `
  -NodePath C:\path\to\node.exe `
  -DotnetPath C:\path\to\dotnet.exe
```

CI uses `-StaticOnly` to build the managed bridge and validate the exact package
lock, schema and source boundary without provider credentials. The full local
audit additionally creates an offline, in-memory SDK session, proves
single-root binding, executes inside/outside path and junction rejection tests,
and completes three turns through the desktop-owned broker. It then stores those
turns under a temporary CurrentUser-DPAPI envelope, starts a fresh runtime from
that store, verifies the restored model context and saves the continuation. The
store probe also rejects a copied workspace envelope and modified ciphertext.
It proves three ordered terminal saves, one resumed-turn save and a forced
autosave failure that closes submissions while preserving sidecar shutdown.
The third ordinary turn executes the real root-confined `read` tool and requires
a second model request. A separate held request proves cancellation through the
concurrent desktop response pump. A dedicated isolated workspace fixture proves
proposal, approval, replay rejection, drift rejection and explicit rejection
through six additional broker requests and zero broker faults. A separate
reviewed-iteration probe proves an approved two-hunk patch between approved
creation and explicit rejection, through ten broker requests and zero broker
faults. The Node probe also covers duplicate/overlapping patch hunks,
exact-capability mismatch, missing files and ambiguous matches. No
path contacts an online model, transports a provider credential, touches
Explorer or writes a production workspace file.
