# cppsbom overview

## Purpose
- Command-line tool that generates an SBOM report for C++ codebases (README: "C++ Software Bill of Materials").
- Current implementation is a .NET CLI (C#) that scans solutions/projects and writes JSON output.

## Tech stack
- C# / .NET SDK project (net9.0, nullable enabled, preview features enabled).
- Logging via Serilog (Console + File sinks).

## Repo layout (current)
- `src/SbomTool/`: main CLI project and source files (Program.cs, SbomGenerator, scanners, etc.).
- `src/SbomTool/bin` and `src/SbomTool/obj` are present in repo (build artifacts).
- Solution file at `cppsbom.sln` references `src/SbomTool/SbomTool.csproj`.

## Entry point
- `Program.Main` parses CLI options, configures Serilog, runs `SbomGenerator`.
- CLI flags (from `CommandLineOptions`): `--root|-r`, `--third-party|-t` (repeatable), `--output|-o`, `--log`, `--help|-h`.

## Notes
- `AGENTS.md` describes C++/CMake structure (`include/`, `tests/`, GoogleTest) that does not exist in the current repo. Treat as guidance only and confirm with the user if C++ scaffolding is planned.