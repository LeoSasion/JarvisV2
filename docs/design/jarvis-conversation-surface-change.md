# Jarvis Pi conversation surface decision

- Date: 2026-08-02
- Surface: `Jarvis.ControlCenter` Pi conversation and runtime inspector
- Mode: Operate
- Concept seed: `32fb29e4`
- Finish review: **PASS**

## Decision

Use the active-turn handoff rail with passed-baton staging. The four-stage
`USER -> PI RUNTIME -> BOUNDED TOOL -> JARVIS` rail makes the one-active-turn
invariant visible, while completed work recedes into a bounded transcript. A
right inspector keeps provider, workspace, tools, checkpoint, credentials and
shutdown posture visible without competing with the conversation.

Selected proposal:
`docs/design/jarvis-conversation-handoff-rail.png`.

The final implementation preserves the incumbent Control Center world: matte
near-black planes, one-pixel blue-gray rules, measured Segoe UI and Consolas,
cyan for the active/ready relationship, amber for transitional states and coral
for faults or unavailable capabilities. It does not add decorative glow,
shadows, chat bubbles or a fake assistant avatar.

## Declined concepts

- `docs/design/jarvis-conversation-transcript-timeline.png` was clear but too
  conventional; the timeline made trace chronology stronger than current turn
  ownership.
- `docs/design/jarvis-conversation-mission-ledger.png` exposed abundant state but
  was too dense for the primary collaboration view and weakened the transcript.

Both remain design evidence, not runtime assets.

## Finish review

**PASS.** The built WPF surface preserves the selected handoff hierarchy,
dominant transcript, restrained state palette and explicit safety posture. It
also improves truthfulness relative to the concept: the deterministic preview
labels the workspace, broker, checkpoint and credential fields as illustrative,
not started or not configured. Send and Cancel expose accessible automation
names, keyboard shortcuts remain visible, and state text accompanies color.

This PASS is a bounded visual and interaction review of the 1440 by 900 preview;
it is not a claim of live Pi authentication, shell activation or a completed
independent accessibility study.

## Evidence

- Final built screenshot:
  `docs/screenshots/jarvis-control-center-pi-conversation.png`
- Selected concept:
  `docs/design/jarvis-conversation-handoff-rail.png`
- Implemented source:
  `src/common/Jarvis.ControlCenter/MainWindow.xaml`
- Runtime and safety disclosure:
  `docs/PI-AGENT-DESKTOP-CONVERSATION-SURFACE.md`
- Durable system rules: `DESIGN.md`

The screenshot is intentionally a no-runtime preview. Its value is deterministic
visual evidence; runtime behavior is validated separately by the Control Center
and Pi host audits.
