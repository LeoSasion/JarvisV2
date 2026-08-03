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

`--minimized` creates the owned window and registers its desktop-presence
boundary, then hides it from the taskbar behind one notification-area icon. It
does not make Jarvis topmost, replace the Shell or weaken orderly shutdown. If
no recent workspace is available, the same hidden instance remains summonable
and opens the ordinary idle surface with an explicit status message.

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

## Desktop summon and exit

The primary Control Center registers `Ctrl+Alt+J` with `RegisterHotKey` against
its one owned WPF window. `MOD_NOREPEAT` prevents a held chord from generating
an activation storm. The chord and the notification-area icon only restore and
foreground that same instance; neither mechanism carries model input or gains
agent authority. If another application already owns the chord, Jarvis stays
available from the notification area and shows the conflict in `WINDOWS
PRESENCE` instead of replacing the other registration.

The notification icon uses the same concentric cyan Jarvis signal as the
Control Center header. Its native menu has exactly two actions: `OPEN JARVIS`
and `EXIT JARVIS`. Closing the owned window hides it and preserves the admitted
Pi runtime. Only the explicit exit action starts the existing orderly shutdown:
reviewed iteration is suspended, submissions quiesce, the active turn is
cancelled, DPAPI state is flushed, and the owned sidecar and broker are released.
The global hot key is unregistered and the notification icon removed as the
process exits.

Notification-area integration is fail-soft. If Windows rejects icon or menu
initialization, Jarvis keeps the same window visible on the taskbar, reports a
localized actionable error, and refuses to hide behind a tray presence that was
not created. The independent global chord still registers when Windows permits
it.

## Attention beacon

The notification-area identity also carries the smallest useful Pi state while
the Control Center is hidden. It remains cyan when ready or working, changes to
an amber diamond when the owner must decide, and changes to a coral cross when
the runtime fails closed. Working uses a directional handoff mark. Shape and
localized text accompany color, so the meaning does not depend on hue alone.

Jarvis may ask Windows to show a native notification only for three meaningful
hidden transitions: a matching active turn completes, owner action becomes
necessary, or a fail-closed stop occurs. Repeated states are deduplicated and a
completed turn restored from disk is not replayed at startup. The notification
contains only a generic localized title and sentence; prompts, model output,
workspace paths, file names and proposal contents never enter the tray signal.
Clicking it restores the same owned Control Center and returns focus to the
conversation input.

The runtime inspector exposes the current attention state and the last hidden
signal as polite UI Automation live regions. Windows remains the authority for
whether a native balloon is visually presented, so Focus Assist or notification
policy can suppress pixels even after Jarvis successfully submits the signal.
This does not grant Pi any notification, foreground or process capability.

## Verification

`DesktopPresenceProbe` uses two in-memory startup values, temporary synthetic PE
files and a random named-event scope. It proves enable, idempotence, moved-path
visibility, disable, exact resume arguments, one-primary admission, secondary
activation, clean reacquisition after disposal, the exact no-repeat
`Ctrl+Alt+J` contract, visible error `1409` conflict handling and hot-key release.
It also creates and disposes all shape-distinct attention icons. A separate
`DesktopAttentionProbe` proves ready/working/completed, owner-action and fault
selection, matching completion delivery, duplicate suppression, restored-turn
startup suppression and content-free signal keys.
The receipt fixes `ProductionStartupStateTouched=false`; the probe never reads
or writes the real Run value and uses a memory-backed hot-key adapter rather
than changing the live desktop registration.

Run it through:

```powershell
pwsh -File .\scripts\Test-ControlCenter.ps1
```

The bounded live Windows check launches one exact built Control Center, proves
that the minimized launch owns `Ctrl+Alt+J`, posts the real `WM_HOTKEY` path,
records foreground and keyboard-focus state (without treating a background
runner's inability to acquire interactive input as product failure), checks
close-to-hide and second-launch activation, invokes `EXIT JARVIS` through UI
Automation, verifies summon restores the previous maximized state, and fails if
forced cleanup was required. It also reads both attention live regions and
verifies that startup did not replay a stale hidden signal:

```powershell
pwsh -File .\scripts\Test-DesktopPresenceLive.ps1 `
  -ExecutablePath .\path\to\jarvis-control-center.exe `
  -DotnetRoot .\path\to\developer-sdk
```

`DotnetRoot` is needed only for a framework-dependent developer build on a
machine without a globally installed Windows Desktop runtime. The portable
package is self-contained and omits it.
