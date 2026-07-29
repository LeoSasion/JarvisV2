# Platform backends

- `windows10/` is the handoff target for new Win10-specific implementations.
- `windows11/` preserves the reviewed Win11 implementation and exact identities.

Backends must never fall through to one another. Unsupported or unknown hosts
fail closed.
