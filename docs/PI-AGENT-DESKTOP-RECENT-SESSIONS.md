# Pi Agent desktop recent-session catalog

`DesktopRecentSessionStore` lets the owner return to recent native Jarvis work
without typing the workspace and provider again. It is a desktop-owned launch
index, not Pi SDK session persistence and not an agent capability.

## Storage boundary

The production catalog is:

```text
%LOCALAPPDATA%\JARVIS2\PiAgent\session-launcher\recent-sessions.j2catalog
```

The protected payload contains at most eight entries. Each entry contains only
the canonical workspace root, the selected provider kind and the last
successful open time. It never contains an API key, prompt, assistant output,
tool result, edit proposal, approval capability or reviewed-iteration policy.

The whole payload is protected with Windows DPAPI using `CurrentUser` and
versioned JARVIS entropy. The visible JSON envelope contains only its schema,
receipt type, revision, entry count, save time and ciphertext. Commits reject
reparse points, use a unique same-directory `WriteThrough` temporary file,
flush to disk and atomically replace the prior catalog. Oversized, malformed,
future-dated, duplicate, unordered, undecryptable or post-decryption-invalid
catalogs fail closed; there is no plaintext fallback.

## One-action resume

The launcher shows the three newest catalog entries. A current path admission
check controls whether each `VERIFY & RESUME` action is enabled. A missing,
moved, protected or reparse-pointed workspace remains visible as
`UNAVAILABLE`; stored history never grants path authority.

Selecting an available entry still runs the complete
`DesktopSessionLaunchAdmission.Admit` boundary. That rechecks the canonical
workspace and the exact packaged/developer Node, Pi and fixed-Git runtime before
starting a process. Only after the owned runtime reaches `Ready` does the
Control Center update the recent catalog. The runtime separately loads the
workspace-bound CurrentUser-DPAPI conversation checkpoint when one exists, so
the launch index cannot forge or decrypt conversation state by itself.

Manual workspace/provider launch remains available when the catalog is absent
or unreadable. An unreadable catalog is not silently overwritten; the active
conversation can still run, but the owner receives a visible recent-work
persistence error and must repair or remove the failed catalog deliberately.

## Diagnostic boundary

`DesktopRecentSessionStoreProbe` uses a unique temporary directory and the real
CurrentUser DPAPI context. It proves encrypted round-trip, monotonic revision,
latest-provider replacement without duplicate workspace entries, absence of
both raw and JSON-escaped workspace paths in the envelope, ciphertext tamper
rejection and cleanup. It does not write the production catalog, contact a
model, start the sidecar, touch Explorer, modify the registry or mutate the
workspace.

The WPF evidence is
`docs/screenshots/jarvis-session-launcher-recent-work.png`. It includes one
available entry and one deliberately unavailable entry so action and failure
states can be reviewed in the same bounded render.
