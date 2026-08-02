# Pi Agent desktop conversation surface

`Jarvis.ControlCenter` now contains the first native product surface that owns
one `PiAgentDesktopRuntime`. It is an ordinary, resizable WPF window. It does
not replace Explorer, inject into the Shell, install Windhawk or clear the
JARVIS2 kill switch.

## What is implemented

The surface makes one active Pi turn visible as a four-stage handoff:

```text
USER -> PI RUNTIME -> BOUNDED TOOL -> JARVIS
```

The UI binds immutable `PiAgentConversationSnapshot` values through
`PiAgentConversationBinding`. It shows retained turns, streamed assistant text,
tool lifecycle, runtime phase, broker counts, checkpoint state, credential
posture and the admitted workspace. `Ctrl+Enter` submits, `Esc` requests
cancellation, and the buttons expose accessible automation names. The composer
labels this ordinary action `SEND ONCE`; `START REVIEWED LOOP` is a separate,
secondary owner-policy action.

When Pi stages an edit, the same turn expands an inline review plane showing
the normalized path, before SHA-256, exact removed text, exact replacement text
and decision state. Reject comes first in keyboard order and performs no write.
Approve Once rechecks the exact file immediately before commit. The composer is
disabled until the owner decides, and Pi cannot operate either control.

Closing a window with an owned runtime first quiesces submissions, cancels an
active turn, waits for its terminal event, flushes the CurrentUser-DPAPI
checkpoint and releases the owned sidecar and broker. The window gives that
orderly path 12 seconds. It never restarts or terminates Explorer.

## Provider boundary

`LocalDiagnosticModelProvider` remains the default. It is local,
deterministic and credential-free. For every admitted user request it asks Pi
for exactly one root-confined `ls` call, then streams a summary of the tool
result.

The opt-in `OpenAiResponsesModelProvider` adds a real production API boundary
inside the desktop process. The setup dialog protects one key with
CurrentUser DPAPI, never shows the previous value and discloses the exact
model, tools, retention and offline-sidecar posture before saving. Production
mode uses `gpt-5.6-sol`, SSE and `store: false`; no live request is made unless
the user explicitly launches with `--provider openai` and submits a prompt.

This proves the real product route without claiming an online model:

```text
WPF command
  -> conversation state
    -> Pi SDK session in the Node sidecar
      -> current-user model-broker pipe
        -> selected desktop provider
          -> validated read or proposal tool event
            -> root-confined Pi tool / structured proposal
              -> streamed broker response
                -> immutable WPF snapshots
                  -> optional desktop-owner one-shot edit decision
                  -> encrypted terminal checkpoint
```

`read`, `grep`, `find`, `ls` and non-mutating `propose_edit` are the only Pi
tools installed in the session and admitted by the production provider. The
diagnostic provider itself requests only `ls`. `bash`, generic `edit` and
`write` are unavailable. No provider credential enters the sidecar and its
model network remains disabled.

## Launch modes

Launching without arguments opens the idle Control Center and admits no
workspace or runtime. Choose `START PI SESSION`, select one local workspace,
and keep the default `LOCAL DIAGNOSTIC` provider for a deterministic
first turn. The modal validates the workspace and fixed portable/developer
runtime before the same window transitions into the conversation. `CONFIGURE
OPENAI` remains available from the idle surface.

The command-line equivalent remains available for automation and diagnostics:

```powershell
jarvis-control-center.exe `
  --conversation `
  --workspace C:\JarvisV2-Windows10 `
  --provider local
```

After the key is explicitly protected from the idle surface, production mode
can be selected in the same launcher. Its command-line equivalent is:

```powershell
jarvis-control-center.exe `
  --conversation `
  --workspace C:\JarvisV2-Windows10 `
  --provider openai
```

The original diagnostic path override remains available for development:

```powershell
jarvis-control-center.exe `
  --diagnostic-conversation `
  --node C:\portable\node.exe `
  --sidecar C:\JarvisV2-Windows10\src\common\Jarvis.PiAgentHost\src\host.mjs `
  --workspace C:\JarvisV2-Windows10
```

The launcher preflights the same boundary before any runtime starts, and the Pi
host independently enforces it again. Protected roots, drive roots, aliases,
reparse points and workspace escapes fail closed.

The proposal and decision contract is described in
`PI-AGENT-WORKSPACE-EDIT-APPROVAL.md`.

The deterministic design-preview mode starts no Pi runtime:

```powershell
jarvis-control-center.exe --capture-preview C:\absolute\preview.png
```

The runtime inspector also contains the reviewed-iteration owner policy. The
operator types a mission in the existing composer, arms it from the inspector,
and can stop or explicitly re-arm an interrupted policy without leaving the
conversation. The same panel names the four-edit limit, six-hour expiry, pinned
HEAD, durable receipt count and current gate state. It does not create a modal
approval surface or displace the transcript.

## Design decision

The chosen structure is an active-turn handoff rail with a passed-baton
staging model. It exposes who owns the turn and allows completed work to recede
into the transcript. The source retains the concept seed `32fb29e4` in both
XAML metadata and compiled code.

Three image-generated proposals were reviewed before implementation:

- selected handoff rail:
  `docs/design/jarvis-conversation-handoff-rail.png`;
- transcript/timeline alternative:
  `docs/design/jarvis-conversation-transcript-timeline.png`;
- mission-ledger alternative:
  `docs/design/jarvis-conversation-mission-ledger.png`.

These PNGs are design evidence, not runtime assets. The implementation keeps
the established matte near-black, measured cyan and restrained line/plane
language of the prior Control Center.

The final 1440 x 900 WPF render is
`docs/screenshots/jarvis-control-center-pi-conversation.png`. It shows the
illustrative preview mode: every workspace, broker, checkpoint and credential
field is explicitly marked as illustrative, not started or not configured.
The reviewed-iteration extension is captured separately at
`docs/screenshots/jarvis-control-center-reviewed-iteration.png`; its policy is
visible in the incumbent inspector while the one-shot edit remains inside the
producing turn.

## Validation

`scripts/Test-ControlCenter.ps1` runs the ordinary
window and no-shell-mutation source gates, it validates the bound transcript,
keyboard and accessibility controls, provider setup, runtime lifecycle,
checkpoint shutdown, portable bootstrap and the visible credential/safety
disclosures. A separate `Jarvis.ControlCenter.Diagnostics` executable runs the
local provider stream probe and runtime-bootstrap probe outside the WPF
lifecycle.

- one exact `ls` tool request;
- multiple text deltas followed by `stop`;
- an explicit missing-production-authentication disclosure;
- `mutationPerformed=false`.

The full Pi sidecar, broker, real read-tool round trip, cancellation, restore,
tamper rejection, offline OpenAI protocol/DPAPI probe and cleanup matrix
remains in `scripts/Test-PiAgentHost.ps1`. The offline provider receipt does
not claim a configured key or live model request.
