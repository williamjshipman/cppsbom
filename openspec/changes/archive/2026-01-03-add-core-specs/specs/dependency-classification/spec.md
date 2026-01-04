## ADDED Requirements

### Requirement: Dependency sources and types
The system SHALL scan project or target source files for `#include`, `#import`, `#pragma comment(lib, ...)`, ProgID literals, and CLSID literals.
The system SHALL classify dependencies into `HeaderInclude`, `ImportDirective`, `StaticLibrary`, and `Com` types based on the source of discovery.

#### Scenario: Source-based dependency types
- **WHEN** a source file contains an `#include` directive
- **THEN** the dependency is recorded as a `HeaderInclude` entry.

#### Scenario: COM dependency types
- **WHEN** COM metadata is resolved for a ProgID or CLSID
- **THEN** the dependency is recorded as a `Com` entry.

### Requirement: Include and import resolution
The system SHALL resolve non-system includes by checking the source file directory first and then include search paths.
The system SHALL resolve system includes by checking include search paths without the source file directory shortcut.
The system SHALL then attempt resolution relative to the project or target directory.
The system SHALL ignore include directives that cannot be resolved to an existing file.
The system SHALL resolve import directives relative to the source file directory and then the project or target directory.
The system SHALL record unresolved imports using the raw import value.
The system SHALL treat resolved dependencies under the root directory as internal, except when they fall under a configured third-party directory.
The system SHALL omit internal include or import dependencies from the SBOM.

#### Scenario: Internal include excluded
- **WHEN** an include resolves to a file under the root directory and outside any third-party directory
- **THEN** the include is omitted from dependency results.

#### Scenario: Unresolved import retained
- **WHEN** an import directive cannot be resolved to a file path
- **THEN** the dependency is recorded using the raw import value.

### Requirement: Include search paths
The system SHALL include the project or target directory as an include search path.
The system SHALL include each third-party root and its `include` subdirectory when it exists.
The system SHALL include only include directory entries that exist on disk.
The system SHALL use Visual Studio `AdditionalIncludeDirectories` entries (after macro expansion) and CMake `target_include_directories` entries as include search paths.

#### Scenario: Existing include paths only
- **WHEN** a configured include directory does not exist on disk
- **THEN** the directory is ignored for include resolution.

### Requirement: Library resolution and classification
The system SHALL extract static library dependencies from Visual Studio `AdditionalDependencies` entries ending in `.lib` and from `#pragma comment(lib, ...)` directives.
The system SHALL ignore Visual Studio dependency entries containing MSBuild macro expressions (`%(`).
The system SHALL resolve library references by searching the project or target directory, the solution or root directory, and configured library search paths.
The system SHALL treat resolved library paths under the root directory as internal, except when they fall under a configured third-party directory.
When scanning Visual Studio projects, the system SHALL omit internal library dependencies.
When scanning CMake targets, the system SHALL retain resolved internal library dependencies.
The system SHALL record unresolved Visual Studio library references using the library filename without extension.
The system SHALL treat CMake `target_link_libraries` entries that look like file paths (contain path separators or end with `.lib`, `.a`, `.dll`, `.so`, or `.dylib`) as static library dependencies.
The system SHALL resolve CMake target link entries to known target identifiers when possible and use the target identifier as the dependency identifier.
The system SHALL record unresolved CMake link entries using the literal entry value.

#### Scenario: Visual Studio internal library omitted
- **WHEN** a Visual Studio static library resolves to a file under the root directory and outside any third-party directory
- **THEN** the library is omitted from dependency results.

#### Scenario: CMake internal library retained
- **WHEN** a CMake link entry resolves to a file under the root directory and outside any third-party directory
- **THEN** the library is recorded as a dependency.

#### Scenario: CMake link to known target
- **WHEN** a CMake link entry resolves to a known target identifier
- **THEN** the dependency is recorded using that target identifier.

### Requirement: Dependency identifiers
The system SHALL build dependency identifiers from resolved file paths using the following rules:
- Paths under a third-party root use `<third-party-root-name>::<first-segment>` where `first-segment` is the first relative path component.
- Paths under the root directory use the root-relative path.
- Paths outside the root use the filename.

#### Scenario: Third-party identifier
- **WHEN** a resolved dependency path is under a third-party root
- **THEN** the identifier is `<third-party-root-name>::<first-segment>`.

### Requirement: COM metadata enrichment
On Windows, the system SHALL resolve ProgID and CLSID values through the COM registry and include the resolved description, in-proc server path, and registry metadata when available.
On non-Windows platforms, the system SHALL skip COM registry resolution.

#### Scenario: Non-Windows COM resolution
- **WHEN** the system runs on a non-Windows platform
- **THEN** COM registry lookups are skipped and no COM dependencies are added.
