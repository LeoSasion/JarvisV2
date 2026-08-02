# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users

The primary user is the owner-operator of JarvisV2, working with the embedded
assistant as a long-running collaborator across desktop conversation, file
work and future self-iteration. Development currently happens on a disposable
Windows 10 virtual machine; the finished product must support both Windows 10
and Windows 11 through separately admitted platform backends.

## Product Purpose

JarvisV2 gives the assistant a persistent, native Windows desktop presence
outside Codex. It combines a desktop-owned Pi Agent runtime for conversation
and work with a coherent control surface and carefully bounded native shell
customization. Success means the assistant can preserve context, act through
reviewed capabilities and present a distinctive desktop experience without
turning safety or recoverability into hidden behavior.

## Positioning

JarvisV2 is not a web dashboard, Electron shell or full-screen desktop
replacement. Its differentiator is the combination of a real native Windows
surface, an embedded desktop-owned agent runtime, shared cross-version product
logic and explicit fail-closed platform adapters for native shell work.

## Operating Context

- The assistant and owner collaborate through a desktop control surface and
  persistent Pi conversation state.
- Shared product logic belongs under `src/common`; Windows 10 and Windows 11
  retain separate admission, compatibility and native transport boundaries.
- Visible design work is reviewed through image proposals before
  implementation when it materially changes the interface.
- Future visual state may synchronize with physical RGB peripherals, but those
  devices are not represented as decorative objects inside the desktop UI.

## Capabilities and Constraints

- Pi Agent conversation history is bounded, workspace-bound and protected with
  Windows CurrentUser DPAPI.
- The desktop may remember up to eight recent workspace/provider/time launch
  hints in a separate CurrentUser-DPAPI catalog. A hint never carries model,
  conversation or approval authority, and every resume reruns admission.
- The sidecar remains offline and receives model work only through a
  desktop-owned current-user transport.
- Pi can read the admitted workspace and stage one exact replacement, one
  2–8-hunk exact patch in a single existing UTF-8 text file, or one missing
  UTF-8 file of at most 16 KiB whose parent directory already exists. Patch
  hunks must be distinct, uniquely matched and non-overlapping, with at most
  16 KiB of combined review text. A proposal performs no write; only the
  desktop owner can select a one-shot approval bound to the explicit operation
  and reviewed before state. Existing-file operations use the complete file
  SHA-256 and one atomic replacement; creation uses a fixed absent-state
  sentinel plus exclusive no-overwrite commit.
- The desktop can arm a reviewed iteration from a clean Git HEAD for at most
  four owner-approved edits and six hours. Each approved write must pass the
  fixed HEAD/path-set/hash/diff/structured-text gate, then pause for a separate
  owner approval of the exact Node test profile pinned in that clean HEAD. The
  desktop launches Node directly without a shell, bounds time and output, and
  reruns the repository gate afterward; only a test pass plus an exact post-run
  repository receipt may return reasoning control to Pi. Pi cannot invoke the
  validator or its approval. Receipts are workspace-bound and protected with
  Windows CurrentUser DPAPI.
- Multi-file atomic transactions, deletes, renames, directory creation, VCS metadata mutation, generic
  `edit`/`write`, shell access, self-authored policy, unattended approval and
  ungated iteration remain outside the admitted capability set.
- Native shell changes remain locked behind exact compatibility, build receipt,
  recovery and one-shot approval gates.
- Windows 10 and Windows 11 share product behavior and visual tokens where
  technically valid, but never share private symbols, selectors or unsupported
  platform assumptions.
- Explorer replacement, global injection, system-file replacement and
  unattended shell restart loops are outside the product boundary.

## Brand Commitments

- Public product name: **JarvisV2**. Internal runtime namespace: `JARVIS2`.
- The established direction is a restrained, monochromatic science-fiction
  interface built from minimal mathematical points, lines and planes.
- Accent color is continuously variable through one global RGB color
  relationship rather than hard-coded per component.
- Glow is a future global effect, not a baked property of every component.
- The long-term visual system includes parameterized particles and
  post-processing inspired by professional 3D, film and game-engine workflows.

## Evidence on Hand

- Current owned WPF surface:
  `src/common/Jarvis.ControlCenter/MainWindow.xaml`
- Reviewed Pi conversation layout proposals under
  `docs/design/jarvis-conversation-*.png`; the selected handoff rail is now the
  native Control Center conversation surface.
- Cross-version product boundary:
  `config/platform-matrix.json`
- Windows 10 owned-window visual proof:
  `docs/WINDOWS10-NATIVE-STYLE-PROBE.md`
- Confirmed global effects-system direction:
  `docs/VISUAL-EFFECTS-SYSTEM-BRIEF.md`
- Pi Agent desktop runtime contracts and audits under `docs/PI-AGENT-*`,
  `config/pi-agent-desktop-host-contract.json` and
  `scripts/Test-PiAgentHost.ps1`

No user research, accessibility study, public benchmark, testimonial or
production adoption evidence has been established; future product copy must
not fabricate it.

## Product Principles

1. Native presence without pretending to replace Windows.
2. Shared intent, platform-specific admission.
3. Capability and persistence are explicit, bounded and recoverable.
4. One coherent visual state drives both software surfaces and future physical
   lighting.
5. Visual ambition is implemented as reusable systems, not one-off component
   effects.
