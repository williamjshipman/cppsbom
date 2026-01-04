## ADDED Requirements

### Requirement: Logging configuration
The system SHALL configure Serilog to write to the console and to a rolling file at the configured log path.
The system SHALL create the log directory when it does not exist.
The system SHALL use daily rolling log files and retain the most recent seven files.
The system SHALL set the minimum log level to Debug.

#### Scenario: Log directory creation
- **WHEN** the configured log path points to a non-existent directory
- **THEN** the directory is created before logging begins.

### Requirement: Operational logging
The system SHALL log startup and completion messages for SBOM generation.
The system SHALL log a fatal error and return a non-zero exit code when SBOM generation fails.
The system SHALL log a warning when COM registry interrogation is skipped on non-Windows platforms.
The system SHALL log an informational message after writing the SBOM output.

#### Scenario: Fatal error
- **WHEN** an exception occurs during SBOM generation
- **THEN** a fatal log entry is written and the process exits with a non-zero code.

#### Scenario: Non-Windows COM logging
- **WHEN** the system runs on a non-Windows platform
- **THEN** a warning is logged indicating COM registry interrogation is skipped.