# Change: Gate file-level dependency data by include-internal

## Status
Ready for review/approval.

## Why
Default SBOM output should avoid internal file-level paths (for example, project files and source/header paths) unless the user opts in with `--include-internal`. Today, CycloneDX output can include those file-level fields even when internal dependencies are excluded.

## What Changes
- When `--include-internal` is not supplied, dependency file-level properties (`cppsbom:sourcePaths`, `cppsbom:resolvedPaths`, `cppsbom:metadata`) are omitted from SBOM output.
- When `--include-internal` is supplied, file-level properties are included for both Visual Studio and CMake scans.
- External dependency components and internal dependency filtering remain unchanged.
- **BREAKING**: CycloneDX output drops file-level dependency properties by default.

## Non-Goals
- Removing first-party components from SPDX or CycloneDX output.

## Impact
- Affected specs: sbom-output
- Affected code: SBOM writers and dependency summarization
- Related changes: add-include-internal-deps (extends flag semantics to file-level metadata)

## Acceptance Criteria
- Without `--include-internal`, CycloneDX output excludes `cppsbom:sourcePaths`, `cppsbom:resolvedPaths`, and `cppsbom:metadata` from dependency components.
- With `--include-internal`, the properties are emitted when data is available.
- SPDX output remains unchanged aside from dependency inclusion already governed by `--include-internal`.
