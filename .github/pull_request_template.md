## What changed

Describe the narrow change and why it is needed.

## Backend or contract scope

- Scope ID (`windows10`, `windows11`, future desktop backend, or
  `shared-contract`):
- Changed implementation roots:
- Visual backend manifest added or changed:
- Shared contract compatibility impact: none
- Other registered backend tests required:

An implementation pull request owns one backend. Keep shared-contract changes
in a dedicated pull request. A new-backend registration may also update that
backend's manifest, platform-matrix entry and CI wiring, but it must consume an
already-reviewed shared contract.

## Host and module boundary

- Host process:
- Module:
- Runtime safety identifiers changed: no

## Verification

- [ ] `Test-PublicationBoundary.ps1`
- [ ] `Test-PlatformLayout.ps1`
- [ ] The changed backend manifest's `ownership.testEntry`
- [ ] Windows-only / N/A: `Test-Project.ps1`
- [ ] Windows-only / N/A: Supervisor Release build: 0 warnings / 0 errors
- [ ] Windows live-only / N/A: the run passed the `AGENTS.md` automated preflight and ended recovered

List exact run IDs, receipt paths and results.

## Safety and recovery

Explain kill-switch, permit, unload-pin and recovery impact. State clearly
whether the evidence is static, compiled or live; for a live run include the
exact target identity, artifact hash and final recovery state.

## Licensing

- [ ] No copied or derived code
- [ ] Provenance locks and notices updated
- [ ] GPL-compatible source and attribution verified
