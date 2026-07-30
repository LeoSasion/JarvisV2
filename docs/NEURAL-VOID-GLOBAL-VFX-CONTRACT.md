# Neural Void global VFX parameter contract

`config/neural-void-global-vfx-contract.json` is the platform-neutral
authoring contract for the future Neural Void particle system and
post-processing compositor. It is shared by the Win10 and Win11 directions.
The current Win10 theme model embeds and compiles the contract, but no renderer
or editor is enabled.

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

The current milestone is ready only for an owned-process renderer prototype.
It cannot mutate Explorer, activate a Shell module or control physical
lighting.
