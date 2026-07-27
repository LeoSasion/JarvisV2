# Phase 14 — Explorer IInspectable Projection Adapter

Status: **OFFLINE PROJECTION MODEL COMPLETE — NO IINSPECTABLE READ**

## Outcome

Phase 14 adds a fixed-width, allocation-free projection adapter between a
future reviewed `IInspectable` bridge and the Phase 13 fingerprint core. The
adapter is compiled into the disk-only controller and TAP artifacts, but no
runtime path calls it.

The compile gate `JARVIS_ENABLE_LIVE_XAML_PROPERTY_READ` is fixed to zero.
Building with any other value fails. There is no Windows Runtime, XAML, COM,
property, endpoint or loader API in the adapter.

## Accepted projection

Every 192-byte snapshot repeats the exact target identity, sequence, surface,
property, instance handle and selector hash. The adapter accepts only:

1. a local `null` value with no runtime class and zero payload; or
2. a local object already proven by the future bridge to have an exact reviewed
   `SolidColorBrush` runtime-class name, with ARGB and opacity from 0 to
   1,000,000.

Default, inherited, style, theme-resource and dynamic-resource origins are
rejected. Strings, markup extensions, acrylic, gradients, images and unknown
objects are rejected. A class enum without an exact class-name match is also
rejected.

Any unsupported or noncanonical representation latches both the adapter and
its owned Phase 13 fingerprint instance into `BLOCKED`.

## Ownership and completion

The adapter owns one fingerprint instance. It forwards only canonical
`null`/solid-color values, stores the nine canonical originals, and reports
complete only when the fingerprint core has accepted all nine observations in
the fixed surface-major/property-minor sequence.

The stored values are bounded restoration inputs for a later offline
transaction model. They are not proof that Explorer was read and do not grant
permission to write any property.

## Integration boundary

- The controller remains `--describe` only.
- The TAP still rejects every `SetSite` call with `E_ACCESSDENIED`.
- Adapter entry points are not DLL exports.
- The endpoint attempt limit remains zero.
- Validation does not load the TAP DLL.
- No `IInspectable`, dependency-property or brush object is acquired.

The next stage may model snapshot-before-write, exact verification and reverse
restore using only these bounded canonical values. Any real property read or
write requires a later, separately reviewed compile gate and exact live
approval.

Phase 14 grants no permission to connect to Explorer, read a property, load a
TAP, start Windhawk, restart Explorer or modify the desktop.
