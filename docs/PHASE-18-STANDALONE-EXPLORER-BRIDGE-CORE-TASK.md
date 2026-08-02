# Phase 18 — standalone Explorer bridge core

Status: **STANDALONE CORE IMPLEMENTED — TRANSPORT AND LIVE CONNECTION ABSENT**

## Outcome

Phase 18 turns the earlier always-inert Explorer bridge ABI model into a real,
standalone native bridge core. Public CI builds an x64 PE DLL and verifies its
four exports:

- `JarvisBridge_QueryContract`;
- `JarvisBridge_Initialize`;
- `JarvisBridge_Quiesce`;
- `JarvisBridge_QueryState`.

The DLL is a lifecycle boundary, not an injector. It does not enumerate or
open processes, install a Windows Hook, allocate remote memory, load another
module, start a service, read the registry, connect to XAML Diagnostics or
change Explorer. It has no `DllMain` implementation. No binary is checked into
the repository.

## Why this moves the native theme path forward

The quarantined Windhawk host cannot express the project's exact-host
boundary because its base engine uses broad process enumeration and global
injection. The accepted replacement design requires a small standalone ABI
whose callback ownership can be reviewed independently from its future
transport and visual implementation.

The earlier `Jarvis.ExplorerBridgeModel` proved only that malformed requests
fail. It deliberately returned `executionUnsupported` for every structurally
valid request and produced no PE module. The Phase 18 core now implements the
state and ownership mechanics a future exact-thread transport must use while
continuing to deny activation.

## Exact admission boundary

The fixed 80-byte initialization request includes:

1. ABI version and structure size;
2. one nonzero Explorer PID and one nonzero Shell UI thread ID;
3. a nonzero session nonce;
4. fresh host-admission, armed kill-switch and one-shot-permit assertions;
5. the exact-thread-only transport scope;
6. a 32-byte immutable settings SHA-256;
7. zeroed reserved fields.

Any mismatch transitions the instance to `Blocked` with pass-through already
enabled. Preparing the same instance twice retires it instead of continuing
with ambiguous ownership.

## Callback ownership and quiesce

The internal core uses fixed storage and atomics only. A future transport
compiled into the same module may publish its exact PID/TID/nonce and acquire
one generation-bound token per callback. Every token has one matching leave.

Quiesce follows this order:

```text
publish pass-through
  -> move Ready/Active to Draining
  -> reject new callbacks
  -> wait for active callback count to reach zero
  -> Quiesced
```

The core does not block a thread, allocate, take a mutex or sleep on this path.
The caller polls the fixed response. If any external callback entry has ever
been published, `modulePinRequired` remains true for the rest of that Explorer
lifetime—even after a complete drain. `unloadPermitted` is then always false.
This is intentionally more conservative than claiming an unsafe DLL unload.

## Deterministic verification

`scripts/Test-ExplorerBridgeCore.ps1` performs nine source/ABI checks. On a
Windows runner with the Visual Studio x64 tools, it additionally:

- compiles and runs a C++20 fault harness;
- covers malformed admission, identity drift, duplicate initialization and
  publication, pre-publication retirement, callback token ownership,
  pass-through-before-drain and concurrent callback/quiesce races;
- builds the standalone DLL with `/W4 /WX`;
- checks the PE export table with `dumpbin`;
- records only the temporary DLL size and SHA-256 before removing all build
  outputs.

Public CI is valid build evidence for this dependency-free bridge core, but it
is not a canonical build or live-host receipt for the existing Windhawk-based
modules.

## Boundary that remains closed

This phase does not provide:

- `SetWindowsHookEx` or any other collector/transport;
- an exported Hook procedure;
- Explorer process discovery or module loading;
- a visual selector, property write or restoration transaction;
- a command that clears the kill switch or creates a permit;
- live Explorer evidence.

The next gate is a separately reviewed, exact-nonzero-thread transport linked
into this core. It must accept an already verified PID/TID only, contain no
process-name enumeration or global scope, publish at most one external entry,
and prove recovery-terminal, source-hash and fixed-target receipts before a
new exact user approval can authorize one live connection.
