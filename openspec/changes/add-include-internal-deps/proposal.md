# Change: Add include-internal flag

## Why
Current SBOM output excludes internal dependencies, which keeps SBOMs focused on external libraries. Some internal workflows need a full dependency view. Add an opt-in flag to include internal dependencies without changing the default external-only behavior.

## What Changes
- Add CLI flag `--include-internal` to include internal dependencies in SBOM output.
- Default remains external-only when the flag is absent.
- Applies to Visual Studio and CMake scan modes.

## Impact
- Affected specs: cli-usage, dependency-classification
- Affected code: src/SbomTool/CommandLineOptions.cs, src/SbomTool/ProjectAnalyzer.cs, src/SbomTool/CMakeTargetAnalyzer.cs
- Related changes: add-core-specs (extends documented baseline behaviors)

## Acceptance Criteria
- CLI accepts `--include-internal` with no value and reports it in usage text.
- When `--include-internal` is absent, internal dependencies are omitted for Visual Studio and CMake scans.
- When `--include-internal` is present, internal dependencies are included for Visual Studio and CMake scans.
