# Pi Agent desktop presence

JarvisV2 can return to the latest admitted desktop conversation after the
current Windows user signs in. This closes the gap between a portable Pi Agent
package and a durable Windows-session presence without granting the model a
startup, registry, process or approval capability.

## Owner flow

The Control Center runtime inspector exposes `WINDOWS PRESENCE`. The owner may
choose `ENABLE AT SIGN-IN` or `DISABLE AT SIGN-IN`; there is no silent default
and no Pi tool for either action. Enabling stores one exact `REG_SZ` value:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\JarvisV2
"C:\exact\path\jarvis-control-center.exe" --resume-latest --minimized
```

The executable must be an existing, non-reparse Windows PE file. The value is
not expandable and never invokes `cmd`, PowerShell or a repository-authored
script. Moving a portable package leaves the old command visibly identified as
another Jarvis location; the owner can disable it before enabling the new one.

## Resume admission

`--resume-latest` opens the encrypted recent-session catalog, selects the
newest currently available workspace hint and reruns
`DesktopSessionLaunchAdmission`. That admission rechecks the canonical
workspace and complete packaged/developer runtime before any broker or sidecar
starts. The conversation checkpoint remains a separate CurrentUser-DPAPI store
and is restored only by the admitted runtime.

`--minimized` changes only initial presentation. It does not hide Jarvis from
the taskbar, make it topmost, create a tray-only process or weaken orderly
shutdown. If no recent workspace is available, the ordinary idle surface stays
available with an explicit status message.

The desktop sidecar request/readiness window is bounded to 25 seconds (with a
30-second admitted ceiling). This accounts for cold Pi dependency loading on a
slow Windows VM while preserving a finite fail-closed startup. Synthetic hung
sidecar scenarios continue to use their explicit one-second fault timeout.

## Single-instance boundary

Normal launches acquire one named auto-reset event scoped by the current
Windows user and the local interactive session. A second launch signals that
event, restores the existing minimized window and exits; it cannot create a
second broker, sidecar or Pi runtime. Preview screenshot commands remain
isolated so deterministic visual QA can run while the product is open.

The named event carries only foreground intent. It contains no prompt,
workspace path, credential, proposal, approval or command payload.

## Verification

`DesktopPresenceProbe` uses two in-memory startup values, temporary synthetic PE
files and a random named-event scope. It proves enable, idempotence, moved-path
visibility, disable, exact resume arguments, one-primary admission, secondary
activation and clean reacquisition after disposal. The receipt fixes
`ProductionStartupStateTouched=false`; the probe never reads or writes the real
Run value.

Run it through:

```powershell
pwsh -File .\scripts\Test-ControlCenter.ps1
```
