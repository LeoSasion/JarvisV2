# Windows 11 native tests

This directory contains the existing offline Win11 Explorer transport, TAP and
transaction harnesses. Passing them does not prove live Explorer behavior.

`jarvis_explorer_bridge_core_harness.cpp` exercises the standalone ABI v2 core
with exact identity, malformed admission, callback ownership, duplicate calls,
permanent post-publication pinning and concurrent quiesce races. It builds a
temporary executable only and never loads the core into Explorer.

`jarvis_explorer_exact_thread_transport_harness.cpp` couples that bridge core
to a fake exact-thread platform. It covers target-validation and install
failure, duplicate calls, callback drain, unhook failure, truthful live-state
propagation and an install/quiesce race. The real Win32 adapter is compiled
separately and never linked or executed by the harness.
