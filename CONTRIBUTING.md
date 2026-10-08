# Contributing to JarvisV2

JarvisV2 modifies native Windows shell processes. Live validation is a supported
development mode on the dedicated Windows 10 VM, but it must remain narrow,
reversible and fail-closed.

Windows 10 is the primary development backend. The repository is structured so
future desktop-environment backends can be developed on separate machines and
merged through pull requests, but no unregistered backend is supported.

## Backend and contract scope

- Give every implementation pull request one backend scope. Do not combine
  Windows 10, Windows 11, GNOME, Plasma or wlroots implementation changes.
- Treat Ubuntu and Arch as validation profiles. A Linux backend is identified
  by its desktop environment and compositor, not its distribution name.
- Keep platform-private source under its own `src/platforms/<backend>` root.
  A backend must not reference another backend's source tree.
- Register an implemented renderer with one uniquely named file under
  `config/visual-backends`; a manifest is a capability and evidence statement,
  not an automatic public-support claim.
- Make shared visual-contract changes in a dedicated contract pull request and
  run every registered backend test. Do not mix a contract-version change with
  an unrelated platform feature.
- The current cross-OS data plane is limited to `jarvis-visual-signal-v1` and
  `jarvis-retained-vector-scene-v1`. `src/common` otherwise remains a
  Windows-family shared tree.
- A shared contract never grants Shell inspection, mutation, injection or
  device-I/O authority. Those capabilities remain platform-owned and require
  their own evidence.

See `docs/ADR-0002-VISUAL-BACKEND-CONFORMANCE.md` for the normative boundary.

## Before opening a pull request

1. State whether the work was tested offline, compiled only, or exercised live.
2. For a live run, satisfy the automated preflight in `AGENTS.md`: exact
   PID/TID/window, current source and artifact hash, armed kill switch, one
   module permit and an active recovery helper.
3. Use only the narrow reviewed JARVIS2 collector/Hook path. Do not use a global
   injector, bypass compatibility/hash gates, replace system files or create an
   unattended Explorer restart loop.
4. Run the checks that are relevant to the changed code:

   ```powershell
   pwsh -NoLogo -NoProfile -File .\scripts\Test-PublicationBoundary.ps1
   pwsh -NoLogo -NoProfile -File .\scripts\Test-PlatformLayout.ps1
   pwsh -NoLogo -NoProfile -File .\scripts\Test-Project.ps1
   ```

   Also run the test named by the changed backend manifest's
   `ownership.testEntry`. Windows-only checks may be reported as not applicable
   by a non-Windows backend, but the two repository-boundary checks above still
   apply.

5. Describe the evidence boundary: static, compiled or live. Never report one
   as another.

The full native test requires the separately provisioned, hash-locked portable
toolchain. It must fail closed when that toolchain is unavailable; do not add a
download or installer fallback.

## Change design

- Target one Windows host process and one module at a time.
- Keep module IDs, state paths and cross-process synchronization names stable.
- Avoid heap allocation, exceptions and blocking work across foreign ABI
  boundaries.
- Do not call external COM, USER32, loader or logging APIs while internal
  bookkeeping locks are held.
- Record every owned resource as released or retained with a reason.
- Preserve permanent module pins whenever callback or unload safety is
  uncertain.

## Licensing and provenance

Contributions are licensed under GPL-3.0. Identify copied or derived code,
record the exact upstream repository and commit, and update
`config/upstream-lock.json` plus `third_party/NOTICE.md`. Do not copy code or
assets from a reference-only project without an explicit compatible license.

## Pull requests

Keep changes narrowly scoped. Include:

- what changed and why;
- the single backend or shared-contract scope and every changed root;
- affected host/module;
- safety and recovery impact;
- checks run and their exact result;
- whether any live validation occurred.

Standing authorization means a passing automated preflight does not require a
second per-command approval. Record the exact host/build identity, target,
commands, result and final recovery state so the run can be reproduced.
