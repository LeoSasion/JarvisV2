# Phase 15 — Explorer Reversible Style Transaction

Status: **OFFLINE TRANSACTION MODEL COMPLETE — NO PLATFORM WRITE**

## Outcome

Phase 15 adds an allocation-free native transaction model around the exact
three surfaces and nine properties prepared by Phases 13 and 14. It models
snapshot-before-write, forward apply, read-after-write verification, a
60-second deadline and strict reverse restoration.

The compile gate `JARVIS_ENABLE_LIVE_XAML_PROPERTY_WRITE` is fixed to zero.
The model contains no Windows, COM, XAML, dependency-property, endpoint,
loader or process API.

## Prepare gate

Preparation requires:

- one admitted and consumed Phase 13 plan whose complete bind matches;
- one complete Phase 14 adapter holding all nine canonical originals;
- the same target identity, tree generation, instance handles and selectors;
- nine canonical styled values whose domain-separated hashes exactly match the
  bind;
- at least one real value change;
- a preparation time inside the capability and a full 60-second deadline that
  does not exceed capability expiry.

All nine originals and styled values are copied before any simulated write can
be recorded.

## Conservative dirty semantics

Apply order is surface-major/property-minor. Before a result or post-read is
trusted, every reported platform write attempt sets its slot in a nine-bit
dirty mask. Therefore all of these require restoration:

- the platform reports write failure;
- a write-success flag is invalid;
- the verification read fails;
- the observed value is unsupported or differs from the styled value;
- apply stops after only part of the nine slots.

The model never interprets a failed API result as proof that no mutation
occurred.

## Restore semantics

Restoration always starts at the highest dirty slot and moves in strict reverse
order. Each restore attempt writes the stored canonical original and requires
a matching post-read before clearing the dirty bit.

A failed write, failed verification or mismatched value keeps the slot dirty
and permits retry of that same slot. Identity, tree-generation, instance or
selector drift performs no modeled write and leaves restoration required.
`RESTORED` is reachable only when the dirty mask is zero.

## Deadline and integration

At the 60-second deadline, a clean prepared plan quiesces. Any dirty plan
enters `RESTORE_REQUIRED`. The controller and TAP disk artifacts contain the
model but do not call it; the TAP still returns `E_ACCESSDENIED` from `SetSite`,
has only two COM exports and is never loaded by validation.

The harness reports simulated write attempts only. `propertyWriteSupported`,
`executionSupported`, `activationPermitted` and `mutationPerformed` remain
false, and `liveExplorer` remains `not-run`.

The next gate is not automatic activation. It is a fresh compatibility and
binary review for a minimal live read-only bridge, followed by a separately
reviewed write/restore adapter and an exact, temporary-window visual approval.

Phase 15 grants no permission to connect, read or write Explorer, start
Windhawk, restart Explorer or modify the desktop.
