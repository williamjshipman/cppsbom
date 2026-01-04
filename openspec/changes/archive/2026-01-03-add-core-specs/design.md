# Design: Core behavior specs

## Context
- cppsbom lacks OpenSpec coverage for core runtime behaviors beyond CMake discovery.
- This change documents existing CLI, Visual Studio discovery, SBOM output, dependency classification, and logging behavior.
- No code changes are intended.

## Goals / Non-Goals
- Goals: codify current behavior as a baseline; keep specs aligned with implementation.
- Non-Goals: change runtime behavior, add new flags, or modify output schemas.

## Decisions
- Treat current code behavior as the source of truth for new requirements.
- Split capabilities into focused specs to keep scope clear.

## Risks / Trade-offs
- Spec drift if behavior changes without updating specs.

## Migration Plan
- No data migration. Archive the change to publish the new specs after approval.

## Open Questions
- None.