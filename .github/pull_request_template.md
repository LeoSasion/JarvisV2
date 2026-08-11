## What changed

Describe the narrow change and why it is needed.

## Host and module boundary

- Host process:
- Module:
- Runtime safety identifiers changed: no

## Verification

- [ ] `Test-PublicationBoundary.ps1`
- [ ] `Test-Project.ps1`
- [ ] Supervisor Release build: 0 warnings / 0 errors
- [ ] Any live run passed the `AGENTS.md` automated preflight and ended recovered

List exact run IDs, receipt paths and results.

## Safety and recovery

Explain kill-switch, permit, unload-pin and recovery impact. State clearly
whether the evidence is static, compiled or live; for a live run include the
exact target identity, artifact hash and final recovery state.

## Licensing

- [ ] No copied or derived code
- [ ] Provenance locks and notices updated
- [ ] GPL-compatible source and attribution verified
