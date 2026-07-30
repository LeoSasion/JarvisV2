# Neural Void global VFX parameter contract

`config/neural-void-global-vfx-contract.json` is the platform-neutral
authoring contract for the future Neural Void particle system and
post-processing compositor. It is shared by the Win10 and Win11 directions.
`src/common/Jarvis.VisualEffects` owns the pure .NET data model, RGB sampler,
visual-signal frame, contract compiler and inert-preset compiler. The current
Win10 theme model embeds the JSON inputs and acts as the first diagnostic
consumer, but no renderer or editor is enabled.

The design follows the parameter vocabulary used by 3D film tools and game
engines:

```text
shared RGB frame
       |
       +-- particle module graph
       |     spawn -> update -> render
       |
       +-- retained vector content
       |
       +-- ordered post-processing stack
```

This lets a later Galaxy View-like editor generate controls from typed
metadata instead of hard-coding one panel for each effect.

## Parameter model

Every parameter declares:

- a stable ID and value type;
- minimum, maximum and default values;
- a display unit;
- enum options where applicable;
- whether it consumes the shared RGB frame.

The supported value types are scalar, integer, range, enum and normalized
curve. Curve data stores time/value pairs and must begin at time zero, end at
time one and remain time-ordered.

The initial particle graph contains five disabled modules:

- emission: rate, burst count and particle budget;
- motion: speed, heading, spread, drag and turbulence;
- lifetime: minimum and maximum duration;
- appearance: shape, blend mode, size, size-over-life, alpha-over-life and
  shared color source;
- trail: duration, width and decay.

The ordered post stack contains five disabled effects:

- bloom;
- feedback trails;
- chromatic aberration;
- displacement;
- color grading.

These nodes define adjustable capability, not an approved visible preset.
Before any renderer or parameter UI changes the desktop appearance, four
visual proposals must be reviewed with the user.

## Shared visual signal

`jarvis-visual-signal-v1` is the cross-version frame boundary. It carries a
monotonic sequence, phase, tempo, transition value and the current RGB accent
in linear sRGB. Its ordered semantic channels are `accent`, `active`, `pulse`,
`warning` and `fault`.

Accent, active and pulse are derived from the same continuous RGB frame.
Warning and fault use isolated safety colors so a custom hue cannot make a
fault indistinguishable from normal activity. Invalid timing, color, channel
or device-I/O data compiles to an all-zero inactive frame.

## Versioned inert preset

`config/neural-void-vfx-preset.json` is schema version 1 of the first preset
data boundary. It selects the balanced quality vocabulary and records 15
typed overrides across the ten modules, but all ten modules are disabled,
runtime execution is false and physical-device I/O is false.

The compiler admits only the current schema version. Unknown versions are
reported as requiring an explicit migration and resolve to a blocked,
inactive preset; there is no best-effort execution. Unknown parameters,
out-of-range values, enabled modules and quality-budget overflow are also
rejected.

## Retained vector scene

`jarvis-retained-vector-scene-v1` is the backend-neutral command buffer for
mathematical geometry. A scene declares its design coordinate space, quality
profile, stable revision and visual-signal binding, then stores deterministically
ordered point, line, polyline, tangent-arc and plane commands.

Commands reference semantic color channels and relative luminance/opacity;
they cannot carry literal RGB colors. Each command is marked `static` or
`per-frame`, which lets a future adapter retain stable geometry and update only
small signal regions. The compiler reports command, vertex, arc, plane and
shared-signal counts against the selected quality budget.

Bitmap requests, runtime-effect requests, unknown color channels, malformed
geometry, unstable ordering and budget drift are rejected. A rejected scene
resolves to an empty low-power scene instead of partial rendering. This
contract does not yet replace the current WPF drawing adapter or change its
pixels; it establishes the common data boundary for future Win10 and Win11
renderers.

## Render order and quality

The contract fixes four composition stages: background particles, retained
vector content, foreground particles and post processing. A deterministic
60 Hz fixed-step clock and explicit random seed are required so screenshots,
tests and future replay are reproducible.

Three budget profiles are defined:

| Profile | Particles | Trail points | Post passes |
| --- | ---: | ---: | ---: |
| low-power | 512 | 4,096 | 2 |
| balanced | 2,048 | 16,384 | 4 |
| cinematic-preview | 8,192 | 65,536 | 8 |

The profile names are shared product vocabulary. Each platform renderer may
later choose a supported profile based on measured frame time; it may not
silently raise a budget.

## Current safety boundary

All particle modules and post effects are disabled by default. The GPU backend
is intentionally unselected, while a deterministic CPU reference remains a
future requirement. Component-local effects, live Shell integration and
physical-device I/O are false.

The compiler rejects:

- enabled-by-default nodes;
- missing, duplicate or reordered modules;
- out-of-range numbers and descending ranges;
- fractional integer defaults;
- malformed or non-monotonic curves;
- enum defaults outside their option set;
- local fixed-color bindings;
- live Shell, device or component-local effect capabilities.
- unknown preset schema versions or parameter IDs;
- enabled preset modules and preset values outside their typed bounds;
- malformed visual-signal channels or RGB/safety-color drift.
- malformed, reordered or over-budget retained vector commands;
- bitmap, literal-color or runtime-effect requests in vector scenes.

The current milestone is ready only for an owned-process renderer prototype.
It cannot mutate Explorer, activate a Shell module or control physical
lighting.
