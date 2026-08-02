# Windows 11 backend

This directory preserves the existing Win11 25H2 native-shell implementation,
offline Explorer models and build-locked Supervisor.

File movement does not change activation status. Existing compatibility,
canonical build and live-authorization gates remain mandatory.

`Jarvis.ExplorerBridgeCore` is the first real standalone PE boundary for the
future exact Explorer host. It implements ABI v2 preparation, exact identity,
atomic callback ownership, pass-through-before-drain and conservative module
pinning. It deliberately contains no Hook installer, loader, process discovery
or visual mutation, so it cannot connect to Explorer by itself. See
`docs/PHASE-18-STANDALONE-EXPLORER-BRIDGE-CORE-TASK.md`.
