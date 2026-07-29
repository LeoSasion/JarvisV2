# Windows 10 backend handoff

No Win10 native backend is implemented yet. This is intentional.

Start with the root `WINDOWS10-HANDOFF.md` and the read-only
`scripts/Inspect-Windows10Host.ps1`. New projects use
`Jarvis.Win10.<Feature>` and must not reuse Win11 private symbols, selectors or
module IDs.

The first implementation should be an own-process visual probe. Do not begin
with Explorer injection or a generalized framework.
