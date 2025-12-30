## Context
cppsbom currently emits SPDX and CycloneDX output without supplier metadata. Supplier details are required by many SBOM consumers for provenance tracking.

## Goals / Non-Goals
- Goals:
  - Support supplier metadata via root `config.json` and CLI flags.
  - Apply supplier data to document-level outputs and non-third-party packages/components.
  - Map supplier fields into SPDX and CycloneDX structures using their supported fields.
- Non-Goals:
  - Persisting supplier metadata into external systems.
  - Auto-detecting suppliers from source content.

## Decisions
- Decision: Read `config.json` from the scan root; CLI flags override config values.
- Decision: Supplier type defaults to `Organization`; supported values are `Organization` and `Person` (case-insensitive). Invalid values are fatal.
- Decision: Supplier fields are optional; only provided fields are emitted in the SBOM.
- Decision: CycloneDX uses `metadata.supplier` and `component.supplier` for non-third-party components; third-party components omit supplier.
- Decision: SPDX uses package `supplier` and `homepage` for non-third-party packages; contact/email/phone are added to a package comment.
- Decision: Supplier details apply to top-level projects/targets and their non-third-party components; third-party classification uses existing `--third-party` roots.

## Risks / Trade-offs
- SPDX lacks a structured contact field; contact/email/phone will be encoded in comments.
- Supplier metadata may be incomplete if inputs are partial, but outputs will omit missing fields rather than error.

## Migration Plan
- Add config parsing and CLI flags with backward-compatible defaults.
- Extend models and writers to emit supplier data in both formats.

## Open Questions
- None.