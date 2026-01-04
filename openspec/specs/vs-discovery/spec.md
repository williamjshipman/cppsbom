# vs-discovery Specification

## Purpose
TBD - created by archiving change add-core-specs. Update Purpose after archive.
## Requirements
### Requirement: Solution discovery
When scan type is Visual Studio, the system SHALL recursively enumerate `.sln` files under the configured root directory.
If no solutions are found, the system SHALL log a warning and continue without analyzing projects.

#### Scenario: No solutions under root
- **WHEN** the root directory contains no `.sln` files
- **THEN** a warning is logged and no Visual Studio projects are analyzed.

### Requirement: Project extraction from solutions
The system SHALL parse each solution file to locate referenced `.vcxproj` paths and resolve them relative to the solution directory.
If a referenced project file does not exist, the system SHALL log a warning and skip that project.

#### Scenario: Missing project reference
- **WHEN** a solution references a `.vcxproj` file that is missing on disk
- **THEN** the project is skipped and a warning is logged.

#### Scenario: Visual Studio scan focus
- **WHEN** scan type is Visual Studio (`--type vs|visualstudio`)
- **THEN** discovery is limited to `.sln` and `.vcxproj` files and ignores CMakeLists.txt files.

