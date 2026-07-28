# Phase 8: Native style lab and desktop host probe

Status: **FIRST BATCH COMPLETE — OWN-PROCESS LIVE / EXPLORER READ-ONLY**

## Objective

Move from screenshots to controlled on-machine evidence without weakening the
global host quarantine.

Phase 8 has two deliberately separate tracks:

1. apply documented DWM non-client attributes to a JarvisV2-owned HWND and
   inspect the real Windows title bar, border, corners and system backdrop;
2. discover the current Explorer desktop host (`SHELLDLL_DefView` and
   `SysListView32`) read-only, without sending messages or changing it.

The first track is live only inside the style-lab process. The second track is
live inspection only. Neither track loads a module into Explorer.

## Safety boundary

- [x] Windhawk remains Stopped / Manual / PID 0.
- [x] `disabled.flag` remains armed and `active-module.txt` remains absent.
- [x] The style lab may obtain only its own HWND.
- [x] The only native mutation API in the style lab is
  `DwmSetWindowAttribute`, bound to that owned HWND.
- [x] The lab remains a normal, taskbar-visible, resizable window.
- [x] Every preset has an in-app `SYSTEM DEFAULT` rollback.
- [x] Closing the lab destroys the styled HWND and leaves no background
  process.
- [x] The desktop probe may call only read-only window discovery APIs.
- [x] The desktop probe contains no `SendMessage`, `PostMessage`,
  `SetWindowLong`, `SetWindowPos`, hook, injection, service or registry API.
- [x] Explorer is not restarted, terminated or injected.

## Native window test matrix

- [x] System default frame.
- [x] Graphite Mica: dark non-client frame, rounded corners, restrained cyan
  border, main-window system backdrop.
- [x] Night Acrylic: dark non-client frame, rounded-small corners, restrained
  amber border, transient-window system backdrop.
- [x] Mica Alt: dark non-client frame, rounded corners, alternate tabbed
  backdrop.
- [x] Every DWM HRESULT is displayed; unsupported attributes fail visibly.

## Desktop readiness evidence

- [x] Identify exactly one live desktop `SHELLDLL_DefView`.
- [x] Identify its `SysListView32` folder view when present.
- [x] Bind the owning PID/TID without process injection or enumeration.
- [x] Emit `mutationSupported=false`, `activationPermitted=false`,
  `mutationPerformed=false`, and `liveExplorer=read-only-inspection`.
- [x] Do not implement a desktop mutation command in this phase.

## Acceptance for the first Phase 8 batch

1. the Native Style Lab builds warning-free and passes its safety audit;
2. a real on-machine window screenshot is captured and visually reviewed;
3. the desktop probe returns one exact host candidate or fails closed;
4. project, publication and native evidence gates remain green;
5. readiness still fails only on the Windhawk host quarantine;
6. all preview processes are closed and the repository is committed cleanly.

## Separate future approval

Changing the real Explorer desktop view is not authorized by this task. A
future desktop-style mutation must be reversible, scoped to the exact observed
PID/TID/HWND set, reviewed with an exact rollback command, and explicitly
approved by the user in that task.
