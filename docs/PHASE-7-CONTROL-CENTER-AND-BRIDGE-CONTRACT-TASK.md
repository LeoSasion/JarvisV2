# Phase 7: Control Center and standalone bridge contract

Status: **COMPLETE — LOCKED / OFFLINE**

## Why this phase exists

Phase 2 through Phase 6 concentrated on fail-closed native lifecycle work.
That work is necessary, but it left no honest, runnable product surface for a
human reviewer. A user could not see what JarvisV2 was becoming without
reading source and receipts.

Phase 7 adds a visible operator surface while preserving the native-shell
boundary:

1. a normal, resizable WPF Control Center window;
2. a read-only presentation of safety posture, module scope and evidence;
3. an explicit offline standalone-bridge contract;
4. no overlay, taskbar replacement, Explorer mutation or live transport.

## Visual direction

The Control Center uses the restrained **silent avionics bay** direction:

- dark, low-glare panels rather than a bright dashboard;
- cyan for healthy identity/evidence;
- amber for quarantine and unresolved gates;
- red only for hard stops;
- readable Segoe UI typography with limited Consolas metadata;
- dense information hierarchy inspired by eDEX-UI, without decorative radar,
  scanning noise, fake telemetry or unreadable microtext.

The interaction skeleton is:

`surface navigation → system posture → evidence → next gate → command dock`

The screenshot is evidence of this ordinary Control Center window only. It
must never be described as a live modified taskbar or Explorer surface.

## Safety boundary

- [x] The app is `WinExe` WPF, not Electron, WebView or a desktop overlay.
- [x] It has a normal bounded window and never uses topmost, transparency,
  click-through or desktop-covering behavior.
- [x] It contains no P/Invoke, process enumeration, service, registry,
  injection, Hook installation, Explorer restart or system mutation API.
- [x] Every visible action is read-only, disabled or navigational.
- [x] The primary state is `LOCKED / FAIL-CLOSED`.
- [x] Windhawk is visibly labelled `QUARANTINED`.
- [x] The current screen clearly distinguishes build evidence from live
  Explorer validation.
- [x] Closing the preview leaves no JarvisV2 process running.

## Control Center deliverables

- [x] Implement `src/common/Jarvis.ControlCenter`.
- [x] Add a deterministic 1440×900 overview layout.
- [x] Show module/surface ownership and the current blocked gate.
- [x] Show the offline Explorer-host model as non-executable.
- [x] Add Release build and static safety checks.
- [x] Add the checks to public CI and publication boundary.
- [x] Capture the actual WPF visual tree at 1440×900 into
  `docs/screenshots/jarvis-control-center-phase7-final.png`.

## Standalone bridge contract

The current Windhawk mod DLL is not a standalone bridge. Phase 7 may define a
portable data/state contract, but it may not implement a loader.

- [x] Define fixed-width ABI version, state and result enums.
- [x] Keep the model free of Windows headers and process APIs.
- [x] Model `QueryContract`, `Initialize`, `Quiesce`, and `QueryState` as
  offline transitions only.
- [x] Initialization must always report execution unsupported in this phase.
- [x] Add a portable fault matrix covering duplicate initialization, zero
  PID/TID, contract drift, quiesce idempotency and activation prohibition.
- [x] Do not build or publish a DLL that can be loaded into Explorer.

## Acceptance

Phase 7 completes only when:

1. the screenshot exists and is labelled honestly;
2. Control Center and bridge-model tests pass;
3. `Test-Project.ps1` remains green;
4. readiness still fails only because host activation is quarantined;
5. Windhawk remains Stopped / Manual / PID 0;
6. M2 remains disabled, kill switch armed and permit absent;
7. the preview process and recovery terminal are closed.
