---
name: "JarvisV2 Control Center"
description: "A bounded native Windows operations console built from matte planes, measured type, and one visible handoff."
colors:
  canvas-void: "#070A0D"
  owned-chrome: "#081014"
  matte-surface: "#0A1014"
  operational-panel: "#0D151A"
  raised-plane: "#101A20"
  structural-rule: "#20323A"
  completed-rule: "#3C5660"
  primary-text: "#E7EFF1"
  muted-text: "#91A4AC"
  faint-text: "#60747C"
  live-cyan: "#55DED3"
  live-cyan-plane: "#112B2C"
  waiting-amber: "#F0B958"
  waiting-amber-plane: "#352817"
  fault-coral: "#F07167"
  fault-coral-plane: "#352020"
typography:
  display:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "22px"
    fontWeight: 300
    lineHeight: 1.2
  title:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "13px"
    fontWeight: 700
    lineHeight: 1.25
  body:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.54
  label:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "11px"
    fontWeight: 600
    lineHeight: 1.35
  mono-label:
    fontFamily: "Consolas, monospace"
    fontSize: "10px"
    fontWeight: 400
    lineHeight: 1.5
rounded:
  control: "2px"
  panel: "3px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "20px"
  xxl: "36px"
components:
  button-primary:
    backgroundColor: "{colors.live-cyan}"
    textColor: "{colors.canvas-void}"
    typography: "{typography.mono-label}"
    rounded: "{rounded.control}"
    padding: "0 16px"
    height: "38px"
  button-secondary:
    backgroundColor: "{colors.raised-plane}"
    textColor: "{colors.primary-text}"
    typography: "{typography.mono-label}"
    rounded: "{rounded.control}"
    padding: "0 16px"
    height: "38px"
  panel:
    backgroundColor: "{colors.operational-panel}"
    textColor: "{colors.primary-text}"
    rounded: "{rounded.panel}"
    padding: "12px"
  handoff-stage-active:
    backgroundColor: "{colors.live-cyan-plane}"
    textColor: "{colors.primary-text}"
    rounded: "{rounded.panel}"
    padding: "10px"
  transcript-turn:
    backgroundColor: "{colors.matte-surface}"
    textColor: "{colors.primary-text}"
    rounded: "{rounded.control}"
    padding: "13px"
---

# Design System: JarvisV2 Control Center

## Overview

**Creative North Star: "The Bounded Handoff Console"**

JarvisV2 is a restrained native operations surface, not a theatrical assistant
avatar and not a generic chat dashboard. Matte near-black planes, mathematical
rules, measured type and sparse state color make the runtime boundary legible.
The interface earns its science-fiction character through precision and visible
system truth rather than ornament.

The signature relationship is one active request moving from operator to Pi,
through an admitted read-or-propose tool, into an exact owner decision, through
a fixed repository and structured-text gate, and back to Pi for at most one next
reasoning turn. The design keeps ownership, capability limits, cancellation,
persistence and shutdown in the same visual field as the conversation.
Completed work recedes; the current owner receives the accent. Automatic
continuation advances reasoning only and never advances approval.

The implemented visual world is evidenced by
`src/common/Jarvis.ControlCenter/MainWindow.xaml` and the final reviewed render
at `docs/screenshots/jarvis-control-center-reviewed-iteration.png`. Its selected
surface concept is `docs/design/jarvis-conversation-handoff-rail.png`; the
durable concept seed is `32fb29e4`. The bounded finish-review verdict is PASS:
the accessibility, owner-action hierarchy and handoff-label clipping findings
are resolved. This is not a claim of an independent accessibility study or live
shell activation.

**Key Characteristics:**

- Native Windows geometry with an ordinary, resizable application boundary.
- Matte tonal depth, one-pixel rules and almost no decorative effects.
- One global accent relationship; semantic amber and coral appear only for
  waiting/aborted and fault/blocked states.
- Human-readable labels paired with compact monospaced operational metadata.
- Safety posture presented as product information, never hidden in a settings
  afterthought.
- Owner review is a visible one-shot decision inside the producing turn, while
  bounded iteration policy remains visible in the runtime inspector.
- Durable receipts and ephemeral proposal authority are visually distinct:
  restart can offer RE-ARM, but it never restores a pending proposal.

## Colors

Near-black neutrals carry the surface; chroma is scarce enough that state color
always means something.

### Primary

- **Live Cyan:** The active-owner, ready-state, primary-action and keyboard-focus
  color. Treat it as the current implementation of one global accent role, so a
  future RGB relationship changes this resource rather than individual controls.
- **Live Cyan Plane:** The restrained active or pressed fill behind cyan rules;
  never expand it into a luminous card field.

### Secondary

- **Waiting Amber:** Starting, stopping, waiting, aborted and owner-decision
  required states. The exact-edit proposal plane uses amber until the owner
  chooses Reject or Approve Once.
- **Fault Coral:** Runtime faults, failed tool calls and explicit unavailable or
  blocked capabilities.

### Neutral

- **Canvas Void / Owned Chrome:** The application canvas and structural chrome.
- **Matte Surface / Operational Panel / Raised Plane:** Tonal layers for the
  transcript, panels, controls and nested tool activity.
- **Structural Rule / Completed Rule:** Default divisions and the slightly
  brighter trace left by completed handoffs.
- **Primary / Muted / Faint Text:** Reading text, supporting labels and tertiary
  metadata. Faint text never carries the only copy for a required decision.

**The Chroma Is State Rule.** Cyan, amber and coral communicate active, transitional
or unsafe state. They are not decoration.

**The One Accent Relationship Rule.** New components consume the global accent
role; they do not introduce their own blues, greens or per-component glow.

## Typography

**Display Font:** Segoe UI (native Windows fallback)

**Body Font:** Segoe UI (native Windows fallback)

**Label/Mono Font:** Consolas (monospace fallback)

**Character:** Segoe UI keeps conversation and controls native and calm. Consolas
turns runtime facts, identifiers, shortcuts and micro-labels into a precise
instrument readout without making prose feel like a terminal.

### Hierarchy

- **Display** (light, 22 device-independent pixels): sparse surface titles only.
- **Title** (bold, 13): product identity and compact high-priority headings.
- **Body** (regular, 13/20): conversation, inspector explanation and operational
  copy; wrap naturally inside the central reading column.
- **Label** (semibold, 11): section names and structural headings.
- **Mono label** (regular, 10/15): states, tool names, keyboard shortcuts, paths,
  identifiers and timestamps. Use 11 only where a control needs added emphasis.

**The Measured Mono Rule.** Monospace labels annotate the system; they do not
replace readable body typography or become oversized cyberpunk decoration.

## Layout

The canonical desktop frame is 1440 by 900 device-independent pixels and remains
usable down to 1180 by 760. A 56-pixel title rail and 50-pixel status dock contain
a three-column workspace: a 204-pixel navigation rail, a fluid conversation field
and a 304-pixel inspector. The inspector may scroll vertically; the transcript
owns the remaining height.

Within the conversation field, the order is fixed: title and shortcuts, four
equal handoff stages, expanding transcript, then a 108-pixel composer. The handoff
stages use narrow 18-pixel separators so ownership reads left to right without a
diagram legend. Primary panel padding is 12-16 pixels; major column and section
gaps are 18-20 pixels; four- and eight-pixel increments handle local rhythm.

Reviewed iteration remains inside this same frame. The proposal occupies a
bounded plane within its producing transcript turn; the owner policy, 0-of-4
counter, six-hour boundary, receipt state and START / RE-ARM / STOP controls live
in the right inspector. The four-stage rail stays four stages wide: its compact
`BOUNDED TOOL` label covers read, find and propose without clipping, while the
owner decision is shown where the exact diff can be reviewed. Transcript and
inspector scroll independently when vertical content grows.

The session launcher is a focused 760-by-730 owned window. Returning work is
shown first as a compact recent-work plane, followed by the numbered manual
workspace and provider sequence. It may display three recent entries even
though the encrypted catalog retains eight. The footer preserves one manual
primary action; each recent row carries its own explicit `VERIFY & RESUME`
action and never bypasses admission.

This is desktop density for pointer and keyboard use. Do not enlarge it into a
touch layout by stretching the same grid; platform adaptations should preserve
the ownership story using native platform structure.

**The Dominant Transcript Rule.** Runtime metadata frames the work, but the
conversation retains the largest flexible region at every admitted window size.

## Elevation & Depth

The system is flat by default and uses tonal layering plus one-pixel borders, not
drop shadows. Canvas, chrome, surface, panel and raised-plane neutrals establish
containment. A brighter rule or dim state plane indicates activity without making
the UI float above Windows.

Glow is reserved for a future global effects layer. It is not a component-level
shadow token and must not be baked into cards, buttons or text.

**The Plane, Not Card Rule.** Create hierarchy by changing a matte plane and its
structural rule. Do not stack soft shadows or glass panels.

## Shapes

Panels use tightly controlled three-pixel corners; buttons, inputs, transcript
turns and nested tool rows use two-pixel corners. One-pixel strokes make the
geometry explicit. Circles are reserved for status dots and the Jarvis identity
mark. Large pills, chat bubbles and ornamental blobs are outside this visual
language.

**The Near-Square Rule.** Operational surfaces should feel machined and bounded;
rounding softens an edge only enough to avoid brittle geometry.

## Components

### Buttons

- **Shape:** Near-square controls with a two-pixel radius; primary actions are
  38 pixels high with at least 96 pixels of width and 16 pixels of horizontal
  padding.
- **Primary:** Live Cyan fill with Canvas Void text. Reserve it for the action
  that advances the active turn.
- **Secondary:** Raised Plane fill, Primary Text and a Completed Rule outline.
- **Hover / Focus:** Hover moves the outline to Live Cyan; keyboard focus uses a
  two-pixel cyan border; pressed state uses Live Cyan Plane. Disabled controls
  return to a low-contrast neutral and an arrow cursor.
- **Window controls:** Transparent 38-by-32 controls use native symbols, a dark
  hover plane and a one-pixel cyan keyboard-focus border.

### Cards / Containers

- **Corner Style:** Three pixels for primary panels and two pixels for nested
  transcript/tool containers.
- **Background:** Operational Panel for bounded regions, Matte Surface for turns
  and Raised Plane for nested activity.
- **Shadow Strategy:** None; use the tonal layers and structural rules defined
  above.
- **Border:** One pixel, Structural Rule at rest and state color only while that
  state is active.
- **Internal Padding:** 12-16 pixels for panels, 10-13 pixels for nested rows.

### Inputs / Fields

- **Style:** Owned Chrome fill, a one-pixel Completed Rule border, Primary Text
  and 12-by-10 internal padding. The composer is a rectangular working field,
  not a rounded chat capsule.
- **Focus:** Use the global Live Cyan relationship and retain a visible text
  caret.
- **Error / Disabled:** Pair Fault Coral or muted neutral treatment with explicit
  text. A disabled preview must state why submission is unavailable.

### Recent-work launcher

The returning-user path is a launch index, not a conversation preview. Each row
shows the workspace name, canonical path, provider and last-opened time. An
available row names the action `VERIFY & RESUME`; a missing, protected or
reparse-pointed path remains visible but disabled and explicitly says
`UNAVAILABLE`. The explanatory copy states that encrypted conversation context
is restored only when present.

Use the incumbent matte plane and one-pixel rule rather than introducing a new
card language. Cyan belongs to the available action and keyboard focus; an
unavailable row recedes through disabled opacity plus its text label. The
manual Workspace / Model path flow stays fully usable when there is no catalog
or its CurrentUser-DPAPI envelope cannot be opened.

**The Hint Is Not Authority Rule.** A remembered path/provider/time entry is
only a convenience hint. Every resume visibly revalidates the workspace and
complete portable runtime before a process starts, and the catalog never
contains credentials, prompts, tool results or approval capability.

### Navigation

The left rail uses ordered two-digit mono indices, Segoe labels and compact
10-11-pixel padding. The active destination receives a dark cyan plane and a
one-pixel cyan leading rule; inactive destinations stay muted and unboxed.

### Active-Turn Handoff Rail

Four equal stages—USER, PI RUNTIME, BOUNDED TOOL and JARVIS—form the signature
component. Only one stage is active. Its border and plane become cyan; completed
stages retain the brighter neutral rule; future stages recede to 72% opacity.
The tool subtitle may name the admitted read/find/propose set, but must stay
compact enough to remain unclipped at the minimum window width. While a proposal
is paused, the rail states in text that the owner holds the one-shot decision.
The ownership progress line, stage label and status text update together so
color is never the sole carrier of progress.

### Transcript Turn

A turn is one bounded record containing operator request, zero or more read-tool
rows, the streamed Jarvis response and an explicit terminal state. Running turns
use a cyan rule, failed turns use coral rule and plane, and aborted turns use an
amber rule. Completed work returns to neutral structure and recedes into history.

### Workspace Edit Review

A staged edit stays inside the turn that produced it; it is not promoted to a
modal or a separate approval application. The review plane uses amber while
waiting, cyan after an exact commit, coral for drift or failure, and neutral
structure after rejection. Every state is named in text. Show the normalized
path, full before SHA-256, exact removed and replacement text, and the explicit
one-shot boundary. Reject precedes Approve Once in keyboard order. Pi cannot
operate either control, and the composer remains disabled until the owner
decides. Approve Once never looks like permission for later proposals: each of
the maximum four applied edits returns to a fresh, owner-operated decision.

**The Owner Holds the Baton Rule.** A proposed edit is neither success nor
execution. Amber, the `OWNER REVIEW REQUIRED` label and the adjacent exact diff
must remain visible until the human chooses Reject or Approve Once.

### Reviewed Iteration Policy

The inspector presents reviewed iteration as a bounded owner policy, not an
agent preference. Its durable summary shows armed/not-armed status, applied
owner approvals out of four, expiry within six hours, pinned clean Git HEAD and
CurrentUser-DPAPI receipt state. START REVIEWED LOOP is available only from an
owner mission in the composer. STOP LOOP is always an owner action. RE-ARM may
appear only for an interrupted, unexpired policy after the full fixed gate is
rerun; restart never restores proposal authority.

After Approve Once, keep the turn paused until the fixed validation evidence is
terminal. The status must distinguish pinned HEAD, exact approved path set,
current file hashes, `git diff --check`, strict JSON parsing and DTD-disabled
XML/XAML/project parsing. A pass may submit one next Pi reasoning turn within the
remaining count and time; a failure stops closed. Durable receipts record the
decision and gate outcome, while proposal contents and capability remain
session-memory-only.

Repository validation directly launches the bundled
`runtime\git\cmd\git.exe` with no shell. No UI label may imply that a workspace
script, build, test target, PowerShell or command prompt was executed by this
gate.

### Runtime Inspector and Status Dock

The inspector exposes provider, access, workspace, reviewed-iteration policy,
active tools, broker, checkpoint, credential posture and orderly shutdown. The
bottom dock repeats the runtime phase and the `NO UNREVIEWED WRITES` boundary.
These are persistent trust surfaces, not optional diagnostics. Action hierarchy
places one-turn Send separately from START REVIEWED LOOP, keeps Reject before
Approve Once, and prevents disabled preview controls from masquerading as active
primary actions.

## Do's and Don'ts

### Do:

- **Do** expose the active owner, admitted capabilities and safe shutdown posture
  in the same viewport as the task.
- **Do** pair every semantic color with a state word, stage name or status icon;
  preserve accessible automation names for Send, Cancel and window controls.
- **Do** preserve `Ctrl+Enter` for submit and `Esc` for cancellation, with visible
  shortcut copy near the conversation title.
- **Do** make preview, offline, unauthenticated, proposal-only and
  owner-approved-write states explicit in plain language.
- **Do** retain the `32fb29e4` seed in surface metadata when deriving variants of
  this handoff concept.
- **Do** show the owner the exact path, before hash, removed text and replacement
  text before every Approve Once decision.
- **Do** show policy state as a maximum of four owner-approved edits within six
  hours; every applied edit still requires its own Approve Once.
- **Do** preserve CurrentUser-DPAPI durable receipts while making proposal
  authority explicitly ephemeral across shutdown and restart.
- **Do** expose the fixed pinned-HEAD, exact-path-set, hash, diff-check, strict
  JSON and safe XML/XAML/project parse gate before automatic reasoning may
  continue.
- **Do** keep recent-work resume one action while naming its revalidation and
  showing unavailable paths instead of silently dropping them.
- **Do** render RE-ARM as a fresh owner action after repository revalidation,
  never as recovery of an earlier proposal.

### Don't:

- **Don't** collapse the workflow into opaque left/right chat bubbles or hide tool
  work behind an undifferentiated typing indicator.
- **Don't** use gradients, glassmorphism, ambient card shadows, per-component neon
  glow, oversized type or decorative 3D assistant imagery.
- **Don't** add accent colors outside the global accent plus the established
  waiting and fault semantics.
- **Don't** let Faint Text carry required instructions, or communicate status by
  color alone.
- **Don't** imply a workspace, credential, network provider, mutation capability
  or running process that the runtime has not actually admitted.
- **Don't** hide a proposed diff, turn approval into a generic confirmation, or
  allow assistant prose to stand in for the structured edit capability.
- **Don't** let Pi press approval controls, approve unattended, author or extend
  the policy, increase its four-edit/six-hour bounds, or describe another
  reasoning turn as an approved write.
- **Don't** restore, replay or imply a pending proposal after restart; only an
  eligible interrupted policy may be re-armed for one fresh bounded turn.
- **Don't** imply that the repository gate invokes a shell or repository-authored
  code: it starts the bundled `runtime\git\cmd\git.exe` directly and validates
  only the fixed Git/hash/diff/structured-text conditions.
- **Don't** treat the recent-session catalog as path admission, checkpoint
  authentication or permission to resume a pending proposal.
- **Don't** expand reviewed iteration into Explorer, registry, service, device,
  Windows system mutation, Git commit/push/merge, or any other shell authority.
