# JARVIS2 Safety Contract

This repository modifies native Windows shell processes. Safety takes priority
over visual progress and development speed.

## Default state: locked

- Treat `%LOCALAPPDATA%\JARVIS2\disabled.flag` as armed by default.
- Treat `%LOCALAPPDATA%\JARVIS2\active-module.txt` as a one-shot capability,
  not a configuration file. The locked state is flag present and permit absent.
- Do not clear the kill switch unless the user explicitly authorizes a live
  activation in the current task after reviewing a fresh compatibility report.
- Do not install, launch, configure, or enable Windhawk on the live system
  without that same explicit authorization.
- Do not inject a module, restart or terminate Explorer, sign out, reboot, or
  modify Windows registry/system files merely to continue development.
- A build, mock, screenshot, or static check never counts as live validation.

## Work allowed while locked

- Read-only host inspection and source research.
- Repository edits, static tests, managed builds, and portable native builds.
- Toolchains must stay portable and outside Windows system directories.
- Arming the kill switch is always allowed; clearing it is not.

## Required live-activation gate

Before asking to activate any module, all of the following must be true:

1. `jarvis-supervisor inspect` reports every compatibility check passed.
2. The kill switch is armed and its path is shown to the user.
3. The exact source hash has a successful fixed-toolchain build receipt.
4. Only one host module is in scope; all other experimental modules are off,
   and the one-shot permit names only that module.
5. A recovery terminal is available and the recovery command is reviewed.
6. The user explicitly approves the exact
   `clear-kill-switch --module <id> --confirm` command and loading that module.

After approval, activate only once and stop immediately on any Explorer crash,
interaction regression, unexpected window, elevated idle CPU, or XAML
Diagnostics conflict. Re-arm the switch before any recovery restart.

## Forbidden shortcuts

- No unattended Explorer restart loops or watchdog auto-restarts.
- No global injection, shell replacement, system-DLL replacement, or broad
  process targeting.
- No `dwm.exe` module on the taskbar development branch.
- No force option that bypasses build, UBR, product-version, or SHA-256 gates.
