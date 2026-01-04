## MODIFIED Requirements

### Requirement: CycloneDX document mapping
The system SHALL emit a CycloneDX JSON document with `bomFormat` `CycloneDX`, `specVersion` `1.5`, and `version` `1`.
The system SHALL set `metadata.timestamp` to the generation time and include a tool component named `cppsbom`.
The system SHALL create a component for each project (type `application`) and for each dependency (type mapped from dependency type).
The system SHALL emit a dependency graph linking each project component to its dependency components.
The system SHALL emit dependency properties `cppsbom:dependencyType` and, when `--include-internal` is supplied and the data is present, `cppsbom:sourcePaths`, `cppsbom:resolvedPaths`, and `cppsbom:metadata`.
The system SHALL map dependency types to CycloneDX component types as follows: `HeaderInclude` -> `file`, `ImportDirective` -> `file`, `StaticLibrary` -> `library`, `Com` -> `library`.

#### Scenario: Dependency properties suppressed by default
- **WHEN** `--include-internal` is not supplied and dependency summaries include source paths, resolved paths, or metadata
- **THEN** CycloneDX components omit `cppsbom:sourcePaths`, `cppsbom:resolvedPaths`, and `cppsbom:metadata`.

#### Scenario: Dependency properties included when internal data is enabled
- **WHEN** `--include-internal` is supplied and dependency summaries include source paths, resolved paths, or metadata
- **THEN** CycloneDX components include matching `cppsbom:` properties with semicolon-separated values.
