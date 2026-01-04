## MODIFIED Requirements

### Requirement: Command-line flags
The system SHALL accept the flags `--root` (`-r`), `--third-party` (`-t`), `--output` (`-o`), `--log`, `--format`, `--type`, `--include-internal`, and `--help` (`-h`).
The system SHALL allow `--third-party` to be specified multiple times.
The system SHALL accept `--format` values `spdx`, `cyclonedx`, and `cdx` (case-insensitive).
The system SHALL accept `--type` values `cmake`, `vs`, and `visualstudio` (case-insensitive).

#### Scenario: Default values
- **WHEN** no CLI flags are supplied
- **THEN** the root is the current working directory, the output path is `<root>/sbom-report.json`, the log path is `<root>/cppsbom.log`, the format is SPDX, the scan type is Visual Studio, and internal dependencies are excluded unless `--include-internal` is supplied.

#### Scenario: Include internal dependencies
- **WHEN** `--include-internal` is supplied
- **THEN** internal dependencies are retained in SBOM output for both Visual Studio and CMake scans.
