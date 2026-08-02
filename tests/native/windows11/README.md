# Windows 11 native tests

This directory contains the existing offline Win11 Explorer transport, TAP and
transaction harnesses. Passing them does not prove live Explorer behavior.

`jarvis_explorer_bridge_core_harness.cpp` exercises the standalone ABI v2 core
with exact identity, malformed admission, callback ownership, duplicate calls,
permanent post-publication pinning and concurrent quiesce races. It builds a
temporary executable only and never loads the core into Explorer.
