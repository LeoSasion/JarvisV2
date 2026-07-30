# Global visual effects system brief

## Status

This brief records the direction confirmed by the owner. It does not authorize
a visible implementation. Any future slice that changes the rendered product
must first present four visual proposals and receive explicit selection.

## Job and audience

The system gives JarvisV2 one coherent visual state that can drive owned
Windows surfaces now and physical RGB devices later. It is for an expert
owner-operator who needs deep control when tuning a look, but should not need
to edit shaders or component code to change color, motion or effect behavior.

The interaction model should feel closer to a restrained 3D or game-engine
effect inspector than a collection of unrelated theme toggles: parameters are
continuous, named by behavior, reusable across effects and saved as presets.

## Selected direction

- The visual foundation is mathematical geometry: points, line segments,
  polylines, planes, fields and signed-distance shapes. Bitmap assets are
  optional inputs, not the default construction material.
- The default composition stays sparse and monochromatic. Hue comes from one
  global color relationship; luminance, opacity and density create hierarchy.
- Particles and post-processing are separate global layers. Glow is a disabled
  post-process capability until a later approved visual slice, never a baked
  property copied into each control.
- Desktop color and physical-device lighting consume the same normalized
  visual signal, while platform and hardware adapters retain independent
  output limits.
- Windows 10 is the baseline renderer. Windows 11 may use additional native
  capabilities only behind its own adapter and must preserve the same authored
  preset semantics.

## System topology

### 1. Visual signal bus

One frame state is the source of truth for accent color and temporal behavior.
It carries a linear color value, intensity, phase, tempo, transition duration
and named event pulses. A/C/D remain recommended color presets, while every
continuous hue between them is valid.

Components consume semantic channels such as `accent`, `active`, `warning` and
`pulse`; they do not own fixed accent colors. Safety and error colors remain
separate semantic channels so an RGB theme cannot make a fault state
indistinguishable from normal operation.

### 2. Vector geometry layer

The lightweight base renderer accepts retained mathematical primitives and
style parameters:

- position, orientation, scale and clipping;
- stroke width, dash pattern, join and cap;
- fill/stroke luminance and opacity relative to the global color;
- deterministic phase and interpolation;
- semantic state and quality tier.

Geometry remains independent from WPF controls so the same authored primitive
can be rendered by a CPU fallback or a later GPU backend.

### 3. Particle and field layer

The future particle model follows the vocabulary of professional 3D and game
tools:

- emitter shape, rate, burst and deterministic seed;
- lifetime, velocity, acceleration, drag and size-over-life;
- color, opacity and width-over-life relative to the signal bus;
- attractor, repulsor, vortex, turbulence and directional fields;
- curve, envelope, low-frequency oscillator, noise and event modulators;
- trail generation and optional bounded collision stages;
- spawn, update, cull and render budgets.

Particle data should favor compact numeric storage and batched evaluation.
Textures, meshes and volumetric stages remain optional extensions rather than
requirements for the first implementation.

### 4. Material and compositing layer

Materials describe how geometry enters the scene: solid, additive, alpha,
masked or line-emissive. They reference shared color channels and normalized
parameters instead of literal per-effect colors. Blend choice, overdraw and
layer order are explicit preset data.

### 5. Post-processing graph

Post effects operate on the composed owned surface, not individual controls.
The graph reserves typed, independently disableable stages for bloom/glow,
vignette, grain, scan treatment, chromatic separation, distortion and temporal
feedback. Every stage exposes:

- enabled state and quality tier;
- bounded scalar/vector parameters with declared units;
- input/output color-space requirements;
- estimated cost and graceful fallback behavior.

No stage may silently enable because a preset was loaded on unsupported
hardware. Unsupported stages fail closed or downgrade through an explicit
quality policy.

### 6. Physical RGB bridge

The future device bridge receives the same frame signal and event pulses but
does not receive UI geometry. Each hardware adapter maps normalized color,
intensity and timing into its own device zones, refresh limits and calibration.
Desktop rendering remains correct when no device is connected, and device
latency never blocks the UI frame.

## Parameter and preset contract

- Parameters have stable identifiers, type, unit, range, default and
  interpolation behavior.
- Continuous controls support direct values plus optional modulators;
  modulation depth and combination mode are explicit.
- Presets are versioned data with schema validation and migrations. They do not
  contain arbitrary executable scripts.
- Random behavior requires a stored seed so previews, tests and recordings are
  reproducible.
- Editing is transactional: a bad preset or unsupported effect leaves the last
  valid visual state intact.
- Automation sources may later include time, audio, system activity, Pi Agent
  state and device feedback, but each source requires an explicit capability
  and rate limit.

## Performance and quality policy

The engine is frame-budgeted rather than effect-count-driven. It must expose
CPU time, GPU time when available, particle count, draw batches, overdraw
estimate and dropped-effect counters. Exact numeric budgets remain open until
the first Windows 10 renderer is profiled on the target VM.

Quality degradation is deterministic:

1. reduce post-process sample quality;
2. reduce particle spawn density;
3. shorten trails and history;
4. disable optional stages;
5. retain core vector geometry, semantic color and interaction feedback.

Color updates and physical-device output use independent clocks so a slow
device SDK cannot reduce desktop responsiveness.

## Current implementation boundary

The next nonvisual foundation may define:

- the shared visual frame state and semantic color channels;
- typed parameter metadata and validated preset data;
- backend-neutral point/line/plane commands;
- disabled post-process slots and performance counters.

It must not yet:

- enable glow, particles or post-processing in the product UI;
- add decorative mouse or keyboard objects to the screen;
- contact a physical RGB device;
- inject rendering into Explorer or another process;
- choose a final effect composition without four visual proposals.

## Open decisions for the first rendered slice

- CPU vector renderer versus GPU-backed renderer after Win10 profiling;
- realistic minimum, typical and maximum particle counts;
- the first four approved motion/effect compositions;
- modulation UI depth for ordinary versus expert operation;
- accessibility controls for motion reduction, flash limits and contrast;
- hardware protocol and device-zone mapping.
