# cmake-discovery Specification

## Purpose
TBD - created by archiving change add-cmake-support. Update Purpose after archive.
## Requirements
### Requirement: Scan mode selection
The system SHALL use `--type` to select the scan mode with supported values `cmake`, `vs`, and `visualstudio`, defaulting to Visual Studio scanning.

#### Scenario: Default scan mode
- **WHEN** `--type` is not specified
- **THEN** the system scans Visual Studio solutions/projects

#### Scenario: Invalid scan mode
- **WHEN** `--type` is set to an unsupported value
- **THEN** the system terminates with a fatal error

#### Scenario: Visual Studio alias
- **WHEN** `--type vs` is specified
- **THEN** the system behaves identically to `--type visualstudio`

#### Scenario: Visual Studio scan focus
- **WHEN** `--type vs` or `--type visualstudio` is specified
- **THEN** the system scans only `.sln` and `.vcxproj` files and ignores `CMakeLists.txt`

#### Scenario: CMake scan mode
- **WHEN** `--type cmake` is specified
- **THEN** the system scans CMake inputs under the root directory and skips Visual Studio scanning

#### Scenario: CMake scan focus
- **WHEN** `--type cmake` is specified
- **THEN** the system scans only `CMakeLists.txt` files and ignores `.sln` and `.vcxproj` files

### Requirement: CMakeLists discovery
The system SHALL discover only `CMakeLists.txt` files reachable from the root `CMakeLists.txt` via `add_subdirectory` when CMake scan mode is selected.

#### Scenario: CMake project found under root
- **WHEN** `--type cmake` is specified and the root `CMakeLists.txt` references a descendant directory via `add_subdirectory`
- **THEN** the system includes the descendant `CMakeLists.txt` in the scan plan

#### Scenario: Non-literal add_subdirectory arguments
- **WHEN** an `add_subdirectory` argument contains variables or generator expressions
- **THEN** the system logs a warning and ignores that entry for reachability

#### Scenario: add_subdirectory outside root
- **WHEN** an `add_subdirectory` path resolves outside of `--root`
- **THEN** the system still includes the referenced `CMakeLists.txt` in the scan plan

#### Scenario: add_subdirectory path normalization
- **WHEN** multiple `add_subdirectory` entries resolve to the same directory after normalizing case and path separators
- **THEN** the system processes that directory only once

#### Scenario: Missing root CMakeLists
- **WHEN** `--type cmake` is specified and the root `CMakeLists.txt` is missing
- **THEN** the system terminates with a fatal error

### Requirement: Target extraction
The system SHALL extract CMake targets defined via `add_executable` and `add_library` (including `OBJECT`, `IMPORTED`, and `INTERFACE` libraries) and associate their sources and linked libraries when those commands are directly specified as literal list items (no variables or generator expressions).

#### Scenario: Target includes linked dependencies
- **WHEN** a target declares linked libraries in `CMakeLists.txt`
- **THEN** those libraries are recorded as dependencies for the target, treating `PUBLIC`, `PRIVATE`, and `INTERFACE` keywords equivalently

#### Scenario: Non-literal target arguments
- **WHEN** a target source or link entry contains variables or generator expressions
- **THEN** the system logs a warning and ignores that entry

#### Scenario: Linked library is a file path
- **WHEN** a linked library entry is a literal file path (contains a path separator and/or ends with `.lib`, `.a`, `.dll`, `.so`, or `.dylib`)
- **THEN** the dependency is recorded as a file dependency rather than a target dependency

#### Scenario: Linked library is a bare filename with extension
- **WHEN** a linked library entry is a bare filename ending with `.lib`, `.a`, `.dll`, `.so`, or `.dylib`
- **THEN** the dependency is recorded as a file dependency rather than a target dependency

#### Scenario: Interface targets included
- **WHEN** a target is declared as `INTERFACE`
- **THEN** the system includes it for SBOM project mapping

#### Scenario: Alias targets dereferenced
- **WHEN** a target is declared as an `ALIAS`
- **THEN** the system uses the underlying non-ALIAS target for identity and dependency mapping

### Requirement: Include directory handling
The system SHALL treat `target_include_directories` as search paths for resolving includes and SHALL NOT record include directories themselves as dependencies. Include directories discovered from CMake are appended after the existing include search paths (project directory and `--third-party` paths), only include directories that exist on disk, and are deduplicated after normalization.

#### Scenario: Include directories used for resolution
- **WHEN** a target declares include directories
- **THEN** those directories are used for include resolution and not listed as dependencies

#### Scenario: Third-party roots apply to CMake scans
- **WHEN** CMake scan mode processes sources or includes under `--third-party` roots (including those outside `--root`)
- **THEN** those paths are treated as third-party for dependency classification

### Requirement: Target identity
The system SHALL deduplicate target names by qualifying them with their CMake directory path relative to `--root` using the format `<relative_directory>::<target_name>`, normalizing the full identifier to lowercase and using `/` as the path separator. Targets in the root directory use `<target_name>` with no directory prefix. If normalization causes identifier collisions, the system SHALL terminate with a fatal error.

#### Scenario: Duplicate target names
- **WHEN** two CMake targets share the same name in different directories
- **THEN** the system assigns distinct project identifiers using the directory-qualified name

#### Scenario: Root-level target identity
- **WHEN** a target is declared in the root `CMakeLists.txt`
- **THEN** the identifier is the target name without a directory prefix

#### Scenario: Identifier collision
- **WHEN** two targets normalize to the same identifier
- **THEN** the system terminates with a fatal error

#### Scenario: Target identifier usage
- **WHEN** the SBOM is generated for CMake targets
- **THEN** the normalized identifier is used for SBOM project keys

### Requirement: SBOM mapping
The system SHALL map each discovered CMake target to a project entry in the SBOM and associate its extracted dependencies.

#### Scenario: SBOM includes CMake targets
- **WHEN** CMake targets are discovered during scanning
- **THEN** the SBOM includes those targets and their dependencies in the project dependency map

### Requirement: Parse error handling
The system SHALL log CMake parse errors at warning level, retain partial results, and continue scanning other inputs for non-root CMake files. Parse errors in the root `CMakeLists.txt` SHALL be fatal.

#### Scenario: Malformed CMake file
- **WHEN** a non-root `CMakeLists.txt` file cannot be parsed
- **THEN** the system logs a warning, keeps any targets/dependencies identified so far from that file, stops processing that file, and proceeds with remaining files

#### Scenario: Malformed root CMake file
- **WHEN** the root `CMakeLists.txt` file cannot be parsed
- **THEN** the system terminates with a fatal error

