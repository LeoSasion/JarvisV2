# M2 interaction and stability checklist

This checklist is for a future separately authorized M2 session. Every row
starts as **not run**. Offline builds must not pre-mark a result.

## Session identity

- Date/time:
- Windows build / UBR:
- Explorer PID and start time:
- M2 source SHA-256:
- canonical run ID:
- readiness receipt:
- operator:

## One-module proof

- [ ] M1 is not loaded.
- [ ] Only `jarvis-taskbar-icon-size` is selected.
- [ ] The permit was consumed once.
- [ ] The M2 DLL mapping matches the canonical artifact.
- [ ] No unexpected Windhawk/Jarvis module is mapped.

## Core interaction

- [ ] Start button click.
- [ ] Win key open/close.
- [ ] Launch, focus, minimize and close task buttons.
- [ ] Drag task-button ordering.
- [ ] Hover thumbnail and preview close button.
- [ ] Jump list.
- [ ] System tray and overflow.
- [ ] Notification area clock/calendar.
- [ ] Keyboard navigation and accessibility focus.
- [ ] File Explorer windows remain independent from the desktop Shell PID.

## Display matrix

Run the interaction set at:

- [ ] 100% DPI.
- [ ] 125% DPI.
- [ ] 150% DPI.
- [ ] 200% DPI.
- [ ] two monitors with equal scaling.
- [ ] two monitors with different scaling.
- [ ] primary-monitor switch.
- [ ] taskbar on each monitor.
- [ ] auto-hide enabled and disabled.
- [ ] full-screen application enter/exit.

## Lifecycle and stability

- [ ] application launch/close churn.
- [ ] sleep/wake.
- [ ] display disconnect/reconnect.
- [ ] re-arm converts M2 to pass-through.
- [ ] settings changes cannot reactivate a quiesced module.
- [ ] new Explorer lifecycle cannot load without a new permit.
- [ ] 25 explicitly controlled Explorer lifecycle repetitions.
- [ ] one-hour idle CPU/memory/handle/thread comparison.
- [ ] no visual residue after disable and recovery.

## Stop record

If any item fails, stop immediately and record:

- first failing action;
- exact time and Explorer PID;
- observed behavior;
- readiness/build receipt hashes;
- whether re-arm succeeded;
- mapping state after re-arm;
- whether a separately approved Explorer recovery was required.

Do not continue the matrix after the first unexplained failure.
