# Change: Add core behavior specs

## Why
cppsbom behavior is largely undocumented in OpenSpec beyond CMake discovery. Capturing current CLI, Visual Studio scanning, SBOM output, dependency classification, and logging behaviors provides a stable baseline for future changes.

## What Changes
- Add OpenSpec capabilities for CLI usage, Visual Studio discovery, SBOM output, dependency classification, and logging.
- Document current behavior without changing implementation.

## Impact
- Affected specs: cli-usage (new), vs-discovery (new), sbom-output (new), dependency-classification (new), logging (new)
- Affected code: none (documentation/spec-only change)

## Acceptance Criteria
- Specs cover current CLI flags, defaults, and validation behavior.
- Specs capture Visual Studio solution/project discovery behavior.
- Specs describe SPDX and CycloneDX output mappings and file-writing behavior.
- Specs document dependency classification rules, including third-party handling.
- Specs describe logging configuration and defaults.