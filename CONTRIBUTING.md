# Contributing to JarvisV2

JarvisV2 modifies native Windows shell processes. A visually attractive change
is not acceptable if it weakens the fail-closed boundary.

## Before opening a pull request

1. Keep `%LOCALAPPDATA%\JARVIS2\disabled.flag` armed.
2. Do not install, start, configure or enable Windhawk for development.
3. Do not inject a module or restart Explorer as part of a build or test.
4. Run:

   ```powershell
   pwsh -NoLogo -NoProfile -File .\scripts\Test-PublicationBoundary.ps1
   pwsh -NoLogo -NoProfile -File .\scripts\Test-Project.ps1
   ```

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
- affected host/module;
- safety and recovery impact;
- checks run and their exact result;
- whether any live validation occurred.

Live validation is never a routine pull-request step. It requires a separate,
current, exact activation approval under `AGENTS.md`.
