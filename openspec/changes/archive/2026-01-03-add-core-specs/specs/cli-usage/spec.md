## ADDED Requirements

### Requirement: Command-line flags
The system SHALL accept the flags `--root` (`-r`), `--third-party` (`-t`), `--output` (`-o`), `--log`, `--format`, `--type`, and `--help` (`-h`).
The system SHALL allow `--third-party` to be specified multiple times.
The system SHALL accept `--format` values `spdx`, `cyclonedx`, and `cdx` (case-insensitive).
The system SHALL accept `--type` values `cmake`, `vs`, and `visualstudio` (case-insensitive).

#### Scenario: Default values
- **WHEN** no CLI flags are supplied
- **THEN** the root is the current working directory, the output path is `<root>/sbom-report.json`, the log path is `<root>/cppsbom.log`, the format is SPDX, and the scan type is Visual Studio.

#### Scenario: Third-party roots
- **WHEN** `--third-party` is supplied with multiple paths
- **THEN** existing directories are retained as third-party roots and missing directories are ignored.

#### Scenario: Help output
- **WHEN** `--help` or `-h` is supplied
- **THEN** usage text is written to stdout and the process exits with code 0.

### Requirement: Argument validation
The system SHALL treat unknown flags or missing flag values as errors.
The system SHALL require the root directory to exist.
The system SHALL reject unknown format or scan type values.
The system SHALL normalize root, output, log, and third-party paths to absolute paths.

#### Scenario: Unknown argument
- **WHEN** an unsupported flag is provided
- **THEN** argument parsing fails with an error.

#### Scenario: Invalid root directory
- **WHEN** `--root` points to a directory that does not exist
- **THEN** argument parsing fails with a directory-not-found error.

#### Scenario: Invalid format
- **WHEN** `--format` is set to a value outside `spdx|cyclonedx|cdx`
- **THEN** argument parsing fails with an error describing expected formats.

#### Scenario: Invalid scan type
- **WHEN** `--type` is set to a value outside `cmake|vs|visualstudio`
- **THEN** argument parsing fails with an error describing expected scan types.