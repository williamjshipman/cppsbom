# Task completion checklist

## Validate
- Build the CLI: `dotnet build cppsbom.sln`.
- If CMake workflow applies, run: `ctest --test-dir build`.
- Run formatting when relevant: `cmake --build build --target clang-format` (C++ guidance).

## SBOM hygiene (from `AGENTS.md`)
- Do not commit proprietary SBOM inputs; redact sensitive metadata.
- Validate exported SBOMs against SPDX schema and capture anomalies in PR notes.
- Audit third-party deps quarterly by regenerating SBOM and documenting version deltas.

## Commits/PRs
- Use imperative, <=50-char subject; prefer Conventional Commit prefixes (`feat:`, `fix:`, `docs:`).
- Add PR notes for behavior changes, schema/CLI flag changes, and validation steps.