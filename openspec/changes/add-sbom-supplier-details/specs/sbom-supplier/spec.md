## ADDED Requirements

### Requirement: Supplier configuration sources
The system SHALL read supplier settings from `config.json` at the scan root under a nested `supplier` object and SHALL allow CLI flags to override config values.

#### Scenario: CLI overrides config
- **WHEN** a supplier field is set in both `config.json` and a CLI flag
- **THEN** the CLI value is used in the SBOM output

#### Scenario: Missing config
- **WHEN** `config.json` is missing
- **THEN** supplier configuration defaults to CLI values only

### Requirement: Supplier type handling
The system SHALL support supplier types `Organization` and `Person` (case-insensitive), defaulting to `Organization` when omitted, and SHALL fail on unsupported values.

#### Scenario: Supplier type default
- **WHEN** supplier type is not provided
- **THEN** the system uses `Organization`

#### Scenario: Invalid supplier type
- **WHEN** supplier type is not `Organization` or `Person`
- **THEN** the system terminates with a fatal error

### Requirement: Optional supplier fields
The system SHALL treat all supplier fields as optional and SHALL omit any missing fields from the SBOM output.

#### Scenario: Partial supplier data
- **WHEN** only some supplier fields are provided
- **THEN** only those fields appear in the SBOM output

### Requirement: CycloneDX supplier mapping
The system SHALL map supplier fields into CycloneDX `metadata.supplier` and SHALL apply the same supplier data to non-third-party components only.

#### Scenario: CycloneDX metadata supplier
- **WHEN** supplier data is provided
- **THEN** CycloneDX output includes `metadata.supplier` with name, url, and contact info

#### Scenario: CycloneDX component supplier
- **WHEN** a component is not third-party
- **THEN** the component includes supplier data

#### Scenario: CycloneDX third-party exclusion
- **WHEN** a component is third-party
- **THEN** the component omits supplier data

### Requirement: SPDX supplier mapping
The system SHALL map supplier fields into SPDX package `supplier` and `homepage` for non-third-party packages and SHALL encode contact/email/phone fields in a package comment.

#### Scenario: SPDX supplier fields
- **WHEN** supplier data is provided for a non-third-party package
- **THEN** the package includes `supplier` (with the configured type), `homepage` (supplier url), and a comment containing contact/email/phone

#### Scenario: SPDX third-party exclusion
- **WHEN** a package is third-party
- **THEN** the package omits supplier data

### Requirement: Supplier scope
The system SHALL apply supplier metadata to top-level projects/targets and other non-third-party packages/components only, using `--third-party` roots to determine exclusions.

#### Scenario: Supplier scope
- **WHEN** a project or target is under a third-party root
- **THEN** supplier metadata is not applied to it