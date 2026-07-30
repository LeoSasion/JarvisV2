# Pi Agent desktop checkpoint store

`PiAgentConversationCheckpointStore` gives the Windows desktop runtime a
durable resume boundary without enabling Pi SDK session files or giving the
Node sidecar access to an encryption key.

## Storage and protection

The production default root is:

```text
%LOCALAPPDATA%\JARVIS2\PiAgent\conversations
```

Each admitted workspace maps to one lowercase SHA-256 filename. The canonical
workspace path is not written into the envelope. The checkpoint JSON is
protected with Windows DPAPI using `CurrentUser`; additional entropy is derived
from a versioned JARVIS domain and the case-normalized absolute workspace root.
Moving a ciphertext envelope to another workspace filename therefore fails
both envelope admission and DPAPI authentication.

The plaintext remains subject to the existing checkpoint limits:

- schema version 1;
- at most 32 completed text turns;
- at most 32,768 serialized UTF-8 bytes;
- at most 16,384 UTF-8 bytes per user or assistant text field.

The encrypted JSON envelope is capped at 65,536 bytes. It contains only the
schema, receipt type, workspace hash, save time and DPAPI ciphertext.

## Commit path

Save operations are serialized per store instance. They:

1. validate and copy the checkpoint;
2. reject reparse points along the storage path;
3. encrypt for the current Windows user and workspace;
4. write a unique same-directory temporary file with `WriteThrough`;
5. flush it to disk;
6. atomically replace the workspace checkpoint;
7. remove a leftover temporary file if the commit fails.

Load operations reject oversized, malformed, wrong-workspace, undecryptable or
post-decryption-invalid envelopes. They never fall back to unencrypted text or
an unbound workspace.

## Runtime lifecycle

`PiAgentDesktopRuntime` loads from the store before starting its broker and
sidecar when no explicit import checkpoint was supplied. Every terminal turn
publishes one storage-agnostic checkpoint to the runtime. The runtime serializes
those saves, skips duplicate values and gives each local commit a five-second
deadline.

During orderly shutdown it:

1. stops new submissions;
2. cancels and drains an active turn;
3. exports completed text turns;
4. waits for queued terminal autosaves and queues the final value if needed;
5. shuts down the sidecar;
6. disposes the sidecar and broker.

The runtime exposes the restored turn count, save count and last store receipt
for desktop diagnostics. The store is caller-supplied and reusable; the runtime
does not own its lifetime. A save failure is latched, closes new conversation
submissions, is surfaced by shutdown and does not prevent the owned sidecar
from shutting down.

Completed turns are therefore durable without waiting for window close. A
process or machine failure can still lose an active turn or a terminal save
that had not reached its atomic replace.

## Diagnostic boundary

The deterministic runtime probe uses a unique directory under the Windows
temporary directory. It proves encrypted round-trip, automatic fresh-runtime
restore, absence of plaintext prompts and workspace paths, workspace-copy
rejection, ciphertext-corruption rejection, three ordered autosaves for three
terminal turns, one continuation autosave, bounded envelopes and cleanup. It
also forces an autosave failure and proves that submissions close, the error is
surfaced and the owned sidecar still shuts down. It does not write the
production LocalAppData store, contact a live model, touch Explorer or perform
system mutation.
