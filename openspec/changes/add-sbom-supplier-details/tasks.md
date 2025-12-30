## 1. Implementation
- [ ] 1.1 Add CLI flags for supplier name/url/email/phone/contact/type and surface them in help text.
- [ ] 1.2 Load `config.json` from the scan root and parse a nested `supplier` object.
- [ ] 1.3 Define supplier model(s) for SPDX and CycloneDX emission and map supplier type rules.
- [ ] 1.4 Apply supplier metadata to CycloneDX `metadata.supplier` and non-third-party components.
- [ ] 1.5 Apply supplier metadata to SPDX non-third-party packages (supplier/homepage + comment).
- [ ] 1.6 Ensure third-party components/packages omit supplier metadata.
- [ ] 1.7 Update documentation and CLI usage examples for supplier configuration.

## 2. Validation
- [ ] 2.1 Tests: config.json parsing and CLI override precedence.
- [ ] 2.2 Tests: supplier type default and invalid-type error.
- [ ] 2.3 Tests: optional field omission in both formats.
- [ ] 2.4 Tests: CycloneDX supplier mapping for metadata/components.
- [ ] 2.5 Tests: SPDX supplier mapping for supplier/homepage/comment.
- [ ] 2.6 Tests: third-party components/packages omit supplier metadata.