# ADR-0002: Visual backend conformance and parallel platform ownership

- Status: accepted
- Date: 2026-09-04
- Primary development backend: `windows10`
- Current workstream owner scope: `windows10`

## Context

JarvisV2 currently has one implemented Neural Void renderer: the Windows 10
owned-process WPF preview. The repository also contains Windows-family desktop
presence, style experiments and offline Explorer transport research. Those
Windows components must not become accidental requirements for a future Linux
renderer.

The repository may later receive pull requests developed concurrently on
different machines for GNOME, Plasma or a specific wlroots compositor. Ubuntu
and Arch are validation profiles, not backend identities: the integration
boundary is the desktop environment and compositor that own the system UI.

Creating a shared runtime renderer interface now would be premature. With only
one implementation, such an interface would likely preserve WPF concepts such
as `DrawingContext`, dispatcher affinity or Windows resource lifetime. The
existing data-plane contracts are the narrower stable boundary:

- `jarvis-visual-signal-v1`;
- `jarvis-retained-vector-scene-v1`.

The wider `neural-void-global-vfx-v1` contract is not admitted as a cross-OS
contract in this revision. Its v1 platform scope and receipts remain bound to
Windows 10 and Windows 11. Any generalization must use a separately reviewed
version rather than silently widening v1.

## Decision

1. Windows 10 remains the primary implementation target until its owned desktop
   experience is complete to the project's evidence standard.
2. A renderer backend registers with one repository-relative JSON manifest
   under `config/visual-backends`. An unregistered backend is unsupported.
3. The manifest describes target identity, owned source/test paths, accepted
   input contracts, capability boundaries and the highest evidence actually
   obtained. Registration does not itself declare public support.
4. The first and currently only manifest is
   `windows10-wpf-owned-preview`. It is owned-process rendering only. It does
   not claim Shell integration, foreign-process code, injection, physical
   device I/O, live Shell evidence or mutation.
5. No Linux backend is registered until the same pull request supplies its
   implementation root, platform matrix entry, conformance manifest and test
   entry. The manifest target OS, desktop environment and compositor must
   exactly match that matrix entry, and its normalized source path must remain
   inside the entry's platform root. Placeholder support entries are
   prohibited.
6. A portable visual contract grants no authority to inspect, inject into or
   mutate a desktop Shell. Each platform backend owns its separate admission,
   lifecycle and recovery policy.

## Pull request ownership

Ordinary implementation pull requests own one backend scope. A Windows 10
change stays under its Windows 10 implementation/test/config/documentation
paths. Future Linux backends should use desktop-environment identities such as
`linux-gnome`, `linux-plasma` or a specific `linux-wlroots-*` backend rather
than distribution names.

A change to a shared contract is a dedicated contract pull request. It must
state compatibility impact and keep every registered backend test green. A
backend must not reference another backend's source tree. Governance changes
that register a new backend may touch the platform matrix and CI, but should
not carry unrelated feature work.

This file-level isolation is designed to let developers work on different
devices and merge through pull requests without sharing platform-private UI or
host assumptions.

## Evidence and support language

Evidence levels remain distinct:

- `static`: structure or source checks only;
- `compiled`: the backend and deterministic scenarios compile and run without
  proving a live desktop result;
- `owned-process-live`: a real owned window was observed;
- `shell-live`: the exact native Shell integration was observed and recovered.

`publicSupportDeclared=true` is valid only for an implemented supported backend
with live evidence. An owned-process result may not be reported as Shell or
injection evidence.

## Consequences

- Win10 feature work continues without waiting for speculative Linux APIs.
- Future backends can consume the same visual signal and retained scene while
  choosing native GNOME, Plasma, wlroots or other rendering/lifecycle models.
- The current `src/common` name continues to mean Windows 10/11 shared code.
  Cross-OS reuse is declared per contract, never inferred from that directory.
- A second real renderer will provide the evidence needed to design any future
  runtime interface. Until then, the manifest is a conformance handshake, not
  an abstraction layer.

## Verification

`scripts/Test-PlatformLayout.ps1` verifies that:

- the matrix and disk name the same Windows-family common projects;
- every backend manifest has a unique file-owned identity;
- the implementation and test paths belong to one registered platform;
- only the admitted visual signal and retained scene contracts cross the
  backend boundary;
- capability, evidence and public-support claims remain coherent;
- the primary backend remains Win10 owned-process rendering without Shell or
  injection authority.
