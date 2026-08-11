# JARVIS2 Windows 10 Autonomy Contract

This repository builds and operates a native Windows desktop theme system.
The machine is a dedicated development VM. Automated live validation on
Windows 10 is part of the normal development workflow.

## Standing authorization

- The user grants standing authorization for JARVIS2 to build, launch, inject,
  observe, screenshot, quiesce, unload, and recover its own reviewed Windows 10
  modules without requesting approval before each run.
- This standing authorization includes automatically clearing and re-arming
  `%LOCALAPPDATA%\JARVIS2\disabled.flag` for a bounded validation session and
  creating or consuming the exact one-shot `active-module.txt` permit.
- The agent may start the JARVIS2 recovery helper and its own controller,
  collector, overlay, preview, and Pi Agent host processes as needed.
- The agent may capture screenshots and diagnostics of JARVIS2 visual results
  after activation without a separate confirmation.
- A successful automated preflight is sufficient authorization. Do not pause
  merely to ask the user to repeat approval for an exact command.

## Automated preflight

Before a live injection, the agent must automatically verify:

1. The host is Windows 10 x64 and the target is one exact live Explorer PID,
   nonzero Shell thread ID, and owned Shell window.
2. The module was built from the current source and its artifact hash is known.
3. The kill switch is initially armed and no stale module permit exists.
4. Only one JARVIS2 injected module is selected for the session.
5. A recovery helper is running and can re-arm the kill switch.

These checks are automated safety prerequisites, not user-confirmation gates.
The compatibility profile may be generated or refreshed for the current
Windows 10 host when exact executable identities and hashes are recorded.

## Injection scope

- Prefer an exact-thread Windows Hook or another narrow, reversible mechanism.
- Target only the reviewed Explorer instance and thread selected by preflight.
- Do not use Windhawk's global service injector. A private JARVIS2 collector
  may load one reviewed module into one exact target.
- Keep callback work bounded, allocation-free where practical, and pass-through
  by default until session admission succeeds.

## Automatic recovery

- Re-arm the kill switch and quiesce the module immediately on an Explorer
  crash, interaction regression, unexpected window, elevated idle CPU,
  callback drain failure, or target identity change.
- Do not create unattended Explorer restart loops. One recovery restart is
  allowed only after the module is quiesced and the kill switch is armed.
- Preserve a diagnostic receipt containing the source hash, artifact hash,
  target PID/TID, timestamps, result, and recovery state.
- End every bounded validation session with the kill switch armed and the
  one-shot permit absent, whether the test succeeds or fails.

## Development freedom

- Repository edits, portable toolchains, builds, automated tests, native
  controllers, collectors, visual experiments, and self-iteration are allowed.
- User-level Windows appearance changes needed for the JARVIS2 experience are
  allowed when reversible and recorded. Do not weaken Windows security,
  replace system files, or disable antivirus protections.
- Large reviewed changes may be committed and pushed directly to GitHub
  `main`, as already authorized by the user.
- Do not access or modify other Git repositories except JARVIS2 and
  DeepFaceLabSN. Do not modify DeepFaceLabSN unless the user explicitly places
  it in scope for a concrete task.

## Truthful reporting

- Clearly distinguish own-process overlays, offline builds, and real injected
  results.
- Never claim live validation or visual mutation from a mock, static check, or
  disk-only DLL.
- When a live run succeeds, capture the requested screenshot and report the
  exact module, target, artifact hash, and final recovery state.
