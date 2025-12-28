# Project Context

## Purpose
- Command-line tool (cppsbom) that generates SBOMs for C++ codebases by scanning Visual Studio solutions/projects and source files.
- Emits SPDX JSON by default and supports CycloneDX JSON via `--format`.

## Tech Stack
- C#/.NET CLI (net9.0, nullable enabled, preview features).
- Serilog (console + rolling file logging) and System.Text.Encoding.CodePages.
- Output formats: SPDX 2.3 JSON and CycloneDX 1.5 JSON.

## Project Conventions

### Code Style
- 4-space indentation with file-scoped namespaces (`namespace CppSbom;`).
- PascalCase for types/methods; camelCase for locals/parameters; prefer `var` when obvious.
- Nullable reference types enabled.

### Architecture Patterns
- `Program` handles CLI parsing/logging and runs `SbomGenerator`.
- `SbomGenerator` orchestrates `SolutionScanner` -> `ProjectAnalyzer` -> `SourceScanner` -> `SbomWriter`.
- OS-specific COM resolution: Windows uses registry; non-Windows uses a null resolver and logs a warning.
- One logical component per `.cs` file under `src/SbomTool/`.

### Testing Strategy
- No test project yet. If tests are added, create `tests/` with a dedicated test project that mirrors the source layout.
- Add regression coverage and negative-path tests for new behavior.

### Git Workflow
- Commit style: imperative subject (<=50 chars) with Conventional Commit prefixes; include a short body for behavior/dependency changes.
- PRs should describe problem/solution/validation and call out schema or CLI flag changes.

## Domain Context
- Scans `.sln` files and `.vcxproj` projects to resolve dependencies from include paths, `#include`, `import`, `#pragma comment(lib, ...)`, and project metadata (AdditionalDependencies/LibraryDirectories).
- Uses COM registry lookups (ProgID/CLSID) on Windows to capture COM dependencies.
- `--root` defines internal scope; `--third-party` marks external dependency roots (non-existent paths are ignored).
- Outputs a dependency summary and project-to-dependency relationships in the SBOM.

## Important Constraints
- Never commit proprietary SBOM inputs; redact sensitive metadata before committing.
- Validate SPDX output against schema when reporting changes.
- COM registry interrogation is Windows-only; non-Windows runs skip with a warning.
- Default log/output paths are under the root directory (rolling logs keep 7 days).

## External Dependencies
- NuGet: Serilog, Serilog.Sinks.Console, Serilog.Sinks.File, System.Text.Encoding.CodePages.
- Windows COM registry (optional, OS-specific).
