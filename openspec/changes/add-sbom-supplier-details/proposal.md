# Change: Add SBOM supplier details

## Why
SBOM consumers often need supplier metadata for provenance and compliance. Providing supplier details in both SPDX and CycloneDX outputs improves downstream tooling compatibility.

## What Changes
- Add `config.json` at the scan root with a nested `supplier` object for name, url, email, phone, contact, and type.
- Add CLI flags `--supplier-name`, `--supplier-url`, `--supplier-email`, `--supplier-phone`, `--supplier-contact`, and `--supplier-type` that override config.json.
- Apply supplier details to document-level and component/package-level outputs, excluding third-party components.
- Map supplier details into SPDX package fields (supplier/homepage + comment) and CycloneDX metadata/components.
- Update CLI help and documentation to describe configuration and flags.

## Impact
- Affected specs: sbom-supplier (new)
- Affected code: `src/SbomTool/CommandLineOptions.cs`, `src/SbomTool/Models.cs`, `src/SbomTool/SbomGenerator.cs`, `src/SbomTool/SbomWriter.cs`, config loading helpers, CLI help/docs

## Acceptance Criteria
- Supplier data can be provided via `config.json` and is overridden by CLI flags.
- Supplier type defaults to `Organization`; unsupported types fail with a fatal error.
- Supplier fields are optional; missing fields are omitted from output.
- CycloneDX output includes supplier details at `metadata.supplier` and for non-third-party components.
- SPDX output includes supplier details on non-third-party packages, maps supplier URL to `homepage`, and includes contact/email/phone in a comment.
- Third-party components/packages do not receive supplier metadata.
- Documentation reflects the new flags and config file behavior.