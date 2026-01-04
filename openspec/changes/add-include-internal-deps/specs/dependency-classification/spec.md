## MODIFIED Requirements

### Requirement: Include and import resolution
The system SHALL resolve non-system includes by checking the source file directory first and then include search paths.
The system SHALL resolve system includes by checking include search paths without the source file directory shortcut.
The system SHALL then attempt resolution relative to the project or target directory.
The system SHALL ignore include directives that cannot be resolved to an existing file.
The system SHALL resolve import directives relative to the source file directory and then the project or target directory.
The system SHALL record unresolved imports using the raw import value.
The system SHALL treat resolved dependencies under the root directory as internal, except when they fall under a configured third-party directory.
The system SHALL omit internal include or import dependencies from the SBOM unless `--include-internal` is supplied.

#### Scenario: Internal include excluded by default
- **WHEN** an include resolves to a file under the root directory and outside any third-party directory and `--include-internal` is not supplied
- **THEN** the include is omitted from dependency results.

#### Scenario: Internal include retained
- **WHEN** an include resolves to a file under the root directory and outside any third-party directory and `--include-internal` is supplied
- **THEN** the include is recorded as a dependency for both Visual Studio and CMake scans.

#### Scenario: Unresolved import retained
- **WHEN** an import directive cannot be resolved to a file path
- **THEN** the dependency is recorded using the raw import value.

### Requirement: Library resolution and classification
The system SHALL extract static library dependencies from Visual Studio `AdditionalDependencies` entries ending in `.lib` and from `#pragma comment(lib, ...)` directives.
The system SHALL ignore Visual Studio dependency entries containing MSBuild macro expressions (`%(`).
The system SHALL resolve library references by searching the project or target directory, the solution or root directory, and configured library search paths.
The system SHALL treat resolved library paths under the root directory as internal, except when they fall under a configured third-party directory, and omit internal library dependencies unless `--include-internal` is supplied.
The system SHALL record unresolved Visual Studio library references using the library filename without extension.
The system SHALL treat CMake `target_link_libraries` entries that look like file paths (contain path separators or end with `.lib`, `.a`, `.dll`, `.so`, or `.dylib`) as static library dependencies.
The system SHALL resolve CMake target link entries to known target identifiers when possible and use the target identifier as the dependency identifier.
The system SHALL record unresolved CMake link entries using the literal entry value.

#### Scenario: Internal library omitted by default
- **WHEN** a static library resolves to a file under the root directory and outside any third-party directory and `--include-internal` is not supplied
- **THEN** the library is omitted from dependency results.

#### Scenario: Internal library retained
- **WHEN** a static library resolves to a file under the root directory and outside any third-party directory and `--include-internal` is supplied
- **THEN** the library is recorded as a dependency for both Visual Studio and CMake scans.

#### Scenario: CMake link to known target
- **WHEN** a CMake link entry resolves to a known target identifier
- **THEN** the dependency is recorded using that target identifier.
