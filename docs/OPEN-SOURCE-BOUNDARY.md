# Open-source publication boundary

Public repository: <https://github.com/LeoSasion/JarvisV2>

## Names

The repository and public project name is **JarvisV2**. Existing runtime
identifiers remain **JARVIS2**:

- `%LOCALAPPDATA%\JARVIS2`;
- `Local\JARVIS2.StateGate.v1`;
- `jarvis-native-taskbar`;
- `jarvis-taskbar-icon-size`;
- existing receipt type names and compatibility profiles.

These identifiers form a fail-closed compatibility contract. Renaming them is
a separate migration, not a cosmetic repository change.

## Included

The public source set contains:

- C++, C# and PowerShell source;
- JSON schemas, compatibility and provenance locks;
- architecture, recovery, safety and validation documentation;
- the complete GPL-3.0 license;
- upstream attribution and a modification record;
- safe public CI that builds the managed Supervisor and checks publication
  boundaries.

## Excluded

The following are never publication inputs:

- `artifacts/` receipts, generated DLLs and logs;
- `tools/` and the locally provisioned Windhawk compiler/runtime;
- `bin/`, `obj/`, IDE state, dumps, traces and test results;
- executable or archive files;
- credentials, local environment files and machine-specific paths.

The publication manifest is
[`config/publication-manifest.json`](../config/publication-manifest.json).
`scripts/Test-PublicationBoundary.ps1` computes the actual candidate set from
Git's exclude rules and fails on boundary violations without printing secret
values.

## CI evidence boundary

Public CI is intentionally narrower than a canonical native receipt:

- it validates the public file boundary;
- parses PowerShell and JSON;
- builds `Jarvis.Supervisor` in Release mode;
- it does **not** download Windhawk, install Windhawk, compile native modules
  with an unreviewed toolchain, inject code or touch Explorer.

Canonical native builds require the separately provisioned toolchain whose
complete input tree is pinned by `config/toolchain-lock.json`. A green public
CI run therefore does not imply `releaseReady`, live validation or activation
permission.

## Licensing

JarvisV2 is distributed under GPL-3.0. Upstream repositories, commits, hashes,
licenses and modification notes are recorded in:

- [`config/upstream-lock.json`](../config/upstream-lock.json);
- [`third_party/NOTICE.md`](../third_party/NOTICE.md).

Reference-only projects contribute no vendored code or assets unless their
license and exact source are separately reviewed.
