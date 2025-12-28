<!-- OPENSPEC:START -->
# OpenSpec Instructions

These instructions are for AI assistants working in this project.

Always open `@/openspec/AGENTS.md` when the request:
- Mentions planning or proposals (words like proposal, spec, change, plan)
- Introduces new capabilities, breaking changes, architecture shifts, or big performance/security work
- Sounds ambiguous and you need the authoritative spec before coding

Use `@/openspec/AGENTS.md` to learn:
- How to create and apply change proposals
- Spec format and conventions
- Project structure and guidelines

Keep this managed block so 'openspec update' can refresh the instructions.

<!-- OPENSPEC:END -->

# Repository Guidelines

## Project Structure & Module Organization
- The solution is `cppsbom.sln` with the main CLI project at `src/SbomTool/SbomTool.csproj`.
- Keep executable code under `src/SbomTool/`, with one logical component per `.cs` file.
- The CLI entrypoint lives in `src/SbomTool/Program.cs`.
- If you add shared helpers, group them by feature under `src/SbomTool/`.
- If tests are added, create a `tests/` directory with a dedicated test project and mirror the source layout.

## Build, Test, and Development Commands
- `dotnet build cppsbom.sln` builds the CLI.
- `dotnet run --project src/SbomTool/SbomTool.csproj -- --help` runs the CLI.
- `dotnet test` runs tests once a test project exists.

## Coding Style & Naming Conventions
- Use C# with four-space indentation and file-scoped namespaces (`namespace CppSbom;`).
- Name types and methods in PascalCase; locals/parameters in camelCase.
- Keep each file focused on a single responsibility; avoid large multi-type files.
- Keep nullable reference types enabled and prefer `var` when the type is obvious.

## Testing Guidelines
- No test project exists yet. If you add tests, keep them in a dedicated test project under `tests/` and mirror the production layout.
- Ensure new features include regression coverage and that negative-path tests accompany validation logic.
- If fixture-heavy scenarios are required, document the dataset provenance in a comment at the top of the test file.
- Aim to keep coverage stable or trending upward; add `// TODO(you):` tags for deliberate gaps.

## CLI & Logging
- Flags: `--root|-r`, `--third-party|-t` (repeatable), `--output|-o`, `--log`, `--help|-h`.
- Defaults: root/current dir, output `sbom-report.json`, log `cppsbom.log` under root.
- Behavior: unknown args throw; `--help` prints usage and exits 0; `--third-party` ignores non-existent paths.
- Logging: Serilog console + rolling file (daily), keeps 7 files; log directory auto-created.
- Example: `dotnet run --project src/SbomTool/SbomTool.csproj -- --root <path> -t <path> -o <file> --log <file>`.

## Commit & Pull Request Guidelines
Craft commit summaries in the imperative mood (50 characters or fewer) and include a short body when behavior changes or dependencies shift. Prefer Conventional Commit prefixes (`feat:`, `fix:`, `docs:`) to aid changelog generation. Pull requests should describe the problem, the solution, and validation steps (`dotnet test` output or reproducible commands). Link issues when relevant, note schema or CLI flag changes explicitly, and attach screenshots for UI or report-format updates.

## Security & SBOM Hygiene
Never check in proprietary SBOM inputs; redact sensitive metadata before committing. Validate exported SBOMs against the SPDX schema using your preferred validator, and capture anomalies in the pull request description. Audit third-party dependencies quarterly by regenerating the SBOM and documenting version deltas.
