# Pi Agent desktop conversation surface

`Jarvis.ControlCenter` now contains the first native product surface that owns
one `PiAgentDesktopRuntime`. It is an ordinary, resizable WPF window. It does
not replace Explorer, inject into the Shell, install Windhawk or clear the
JARVIS2 kill switch.

## What is implemented

The surface makes one active Pi turn visible as a four-stage handoff:

```text
USER -> PI RUNTIME -> READ TOOL -> JARVIS
```

The UI binds immutable `PiAgentConversationSnapshot` values through
`PiAgentConversationBinding`. It shows retained turns, streamed assistant text,
tool lifecycle, runtime phase, broker counts, checkpoint state, credential
posture and the admitted workspace. `Ctrl+Enter` submits, `Esc` requests
cancellation, and the buttons expose accessible automation names.

Closing a window with an owned runtime first quiesces submissions, cancels an
active turn, waits for its terminal event, flushes the CurrentUser-DPAPI
checkpoint and releases the owned sidecar and broker. The window gives that
orderly path 12 seconds. It never restarts or terminates Explorer.

## Honest provider boundary

This slice deliberately uses `LocalDiagnosticModelProvider`. The provider is
local, deterministic and credential-free. For every admitted user request it
asks Pi for exactly one root-confined `ls` call, then streams a summary of the
tool result. The visible response states that production model authentication
is not configured.

This proves the real product route without claiming an online model:

```text
WPF command
  -> conversation state
    -> Pi SDK session in the Node sidecar
      -> current-user model-broker pipe
        -> local diagnostic provider
          -> validated ls tool event
            -> root-confined Pi tool
              -> streamed broker response
                -> immutable WPF snapshots
                  -> encrypted terminal checkpoint
```

`read`, `grep`, `find` and `ls` remain the only Pi tools installed in the
session. The diagnostic provider itself requests only `ls`. `bash`, `edit` and
`write` are unavailable. No provider credential enters the sidecar and its
model network remains disabled.

## Launch modes

Launching without arguments opens an idle surface and admits no workspace or
runtime. A diagnostic conversation requires explicit absolute paths:

```powershell
jarvis-control-center.exe `
  --diagnostic-conversation `
  --node C:\portable\node.exe `
  --sidecar C:\JarvisV2-Windows10\src\common\Jarvis.PiAgentHost\src\host.mjs `
  --workspace C:\JarvisV2-Windows10
```

The workspace still passes the Pi host's canonical-root admission. Protected
roots, drive roots, aliases, reparse points and workspace escapes fail closed.

The deterministic design-preview mode starts no Pi runtime:

```powershell
jarvis-control-center.exe --capture-preview C:\absolute\preview.png
```

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

## Validation

`scripts/Test-ControlCenter.ps1` runs ten checks. In addition to the ordinary
window and no-shell-mutation source gates, it validates the bound transcript,
keyboard and accessibility controls, runtime lifecycle, checkpoint shutdown
and the visible credential/safety disclosures. A separate
`Jarvis.ControlCenter.Diagnostics` executable runs the provider stream probe
outside the WPF lifecycle and requires:

- one exact `ls` tool request;
- multiple text deltas followed by `stop`;
- an explicit missing-production-authentication disclosure;
- `mutationPerformed=false`.

The full Pi sidecar, broker, real read-tool round trip, cancellation, restore,
tamper rejection and cleanup matrix remains in `scripts/Test-PiAgentHost.ps1`.
An authenticated production provider is the next separate reviewed boundary;
this surface does not imply that it exists.
