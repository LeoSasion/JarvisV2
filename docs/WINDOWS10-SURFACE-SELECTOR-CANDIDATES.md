# Windows 10 Shell surface selector candidates

This slice converts the sanitized class topology observed on the exact
`win10-22h2-19045.6466-x64` host into eight offline selector candidates:

- desktop icon list;
- Explorer command bar, content host and folder view;
- taskbar Start button, task list, notification area and clock.

The candidate file is
`config/windows10-surface-selector-candidate.json`. Every selector is an exact
root-to-target class path with a required visibility state and an expected
match count of one. The source evidence excerpt is
`tests/native/windows10/fixtures/win10-22h2-19045.6466-shell-selector-evidence.json`.
It contains no HWND, PID, thread ID, title, path, rectangle or UI text.

## Run the offline gate

```powershell
pwsh -NoLogo -NoProfile -File `
  .\scripts\Test-Windows10SurfaceSelectorModel.ps1 `
  -DotnetPath C:\path\to\dotnet.exe
```

The model compiles the embedded candidate against the embedded evidence and
then runs fail-closed drift scenarios. It has no native imports, process
access, registry access, visual style values or execution transport.

## Boundary

This is not a visual implementation and not live validation. It does not
contact or modify Explorer, the taskbar or desktop, and it cannot activate a
module. `styleValuesDefined`, `executionSupported`, `mutationSupported` and
`activationPermitted` remain false; `liveExplorer` remains `not-run`.

Before any color, material, opacity, spacing, icon-size or layout intent is
implemented, four image concepts must be prepared and one must be explicitly
approved by the product owner. Selector compilation alone does not authorize
that next step.

That gate was fulfilled on 2026-07-30. Concept D / Neural Void was selected
for the desktop composition; concepts A, C and D supply recommended RGB
accent colors. The approved offline contract is documented in
`docs/WINDOWS10-NEURAL-VOID-RGB-INTENT.md`.
