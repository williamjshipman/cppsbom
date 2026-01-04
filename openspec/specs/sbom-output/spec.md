# sbom-output Specification

## Purpose
TBD - created by archiving change add-core-specs. Update Purpose after archive.
## Requirements
### Requirement: Output serialization
The system SHALL serialize SBOM output as indented JSON and omit null values.
The system SHALL create the output directory when it does not exist.
The system SHALL log the output path after writing the SBOM.

#### Scenario: Output directory creation
- **WHEN** the configured output path points to a non-existent directory
- **THEN** the directory is created and the SBOM JSON is written.

### Requirement: SPDX document mapping
The system SHALL emit an SPDX JSON document with `spdxVersion` `SPDX-2.3`, `dataLicense` `CC0-1.0`, `SPDXID` `SPDXRef-DOCUMENT`, and `name` `cppsbom`.
The system SHALL populate `documentNamespace` with the value `https://spdx.org/spdxdocs/cppsbom-<guid>` where `<guid>` is a generated identifier.
The system SHALL populate `creationInfo` with a creator entry `Tool: cppsbom` and the generation timestamp.
The system SHALL represent each project and dependency as a package with `filesAnalyzed` false and license fields set to `NOASSERTION`.
The system SHALL add a `DESCRIBES` relationship from the document to each project package and a `DEPENDS_ON` relationship from each project package to its dependency packages.
The system SHALL include dependency descriptions when available.

#### Scenario: Project and dependency relationships
- **WHEN** a project depends on one or more dependencies
- **THEN** the SPDX output includes a package per project and dependency and links them with `DEPENDS_ON` relationships.

### Requirement: CycloneDX document mapping
The system SHALL emit a CycloneDX JSON document with `bomFormat` `CycloneDX`, `specVersion` `1.5`, and `version` `1`.
The system SHALL set `metadata.timestamp` to the generation time and include a tool component named `cppsbom`.
The system SHALL create a component for each project (type `application`) and for each dependency (type mapped from dependency type).
The system SHALL emit a dependency graph linking each project component to its dependency components.
The system SHALL emit dependency properties `cppsbom:dependencyType` and, when present, `cppsbom:sourcePaths`, `cppsbom:resolvedPaths`, and `cppsbom:metadata`.
The system SHALL map dependency types to CycloneDX component types as follows: `HeaderInclude` -> `file`, `ImportDirective` -> `file`, `StaticLibrary` -> `library`, `Com` -> `library`.

#### Scenario: Dependency properties
- **WHEN** dependency summaries include source paths, resolved paths, or metadata
- **THEN** CycloneDX components include matching `cppsbom:` properties with semicolon-separated values.

