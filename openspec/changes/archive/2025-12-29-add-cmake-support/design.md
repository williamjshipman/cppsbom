## Context
cppsbom currently scans Visual Studio solutions/projects to discover C++ dependencies. CMake is the dominant cross-platform build system for C++ and is commonly used without .sln/.vcxproj files.

## Goals / Non-Goals
- Goals:
  - Support CMakeLists.txt discovery under `--root` when `--type cmake` is selected.
  - Extract CMake target inputs (sources, include paths, link libraries) for SBOM dependency reporting.
  - Integrate CMake-derived data into existing SBOM formats without breaking existing flows.
- Non-Goals:
  - Full CMake language evaluation or build graph execution.
  - Running CMake configure/generate steps automatically.

## Decisions
- Decision: Implement a lightweight CMake parser that recognizes a limited set of commands (e.g., `add_library`, `add_executable`, `target_sources`, `target_include_directories`, `target_link_libraries`, `add_subdirectory`).
- Decision: Represent each discovered CMake target as a project entry in the SBOM dependency map.
- Decision: Add `--type` with values `cmake`, `vs`, `visualstudio` to select scan mode; default is Visual Studio.
- Decision: Each scan mode only processes its own file types (Visual Studio: `.sln`/`.vcxproj`; CMake: `CMakeLists.txt`), ignoring others.
- Decision: Only traverse `CMakeLists.txt` files reachable via `add_subdirectory` from the root file, normalizing paths (case + separators) before deduping.
- Decision: `--type cmake` requires a root `CMakeLists.txt`; missing root is a fatal error.
- Decision: Deduplicate target names using `<relative_directory>::<target_name>`, lowercasing the full identifier and using `/` separators; root targets use just `<target_name>`. Normalized identifier collisions are fatal.
- Decision: Treat include directories as search paths for resolution, not as dependencies themselves.
- Decision: CMake include directories are appended after existing include search paths (project directory and `--third-party` paths) and only included if they exist on disk.
- Decision: Treat `PUBLIC`, `PRIVATE`, and `INTERFACE` link keywords equivalently and ignore generator expressions/variables (log warnings for non-literal entries).
- Decision: Include `OBJECT`, `IMPORTED`, and `INTERFACE` targets; dereference `ALIAS` targets to their underlying target.
- Decision: Literal link entries that look like file paths (contain a path separator and/or end with `.lib`, `.a`, `.dll`, `.so`, `.dylib`) are treated as file dependencies.
- Decision: CMake parse errors are non-fatal for non-root files (warning + retain partial results), but root `CMakeLists.txt` parse errors are fatal.
- Alternatives considered: Invoking CMake to export compile commands; rejected for now due to environment complexity and side effects.

## Risks / Trade-offs
- Parsing may miss dependencies expressed via complex CMake logic/macros.
- Mapping CMake targets to filesystem paths may be ambiguous without running configure steps.
- Additional parsing must avoid false positives that inflate dependency lists.

## Migration Plan
- Add CMake discovery and parsing behind a new `--type cmake` scan mode.
- If new CLI flags are introduced, default behavior remains backward-compatible.

## Open Questions
- Is parsing `compile_commands.json` a preferred future enhancement?
