# Contributing to JarvisV2

JarvisV2 modifies native Windows shell processes. Live validation is a supported
development mode on the dedicated Windows 10 VM, but it must remain narrow,
reversible and fail-closed.

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

Standing authorization means a passing automated preflight does not require a
second per-command approval. Record the exact host/build identity, target,
commands, result and final recovery state so the run can be reproduced.
