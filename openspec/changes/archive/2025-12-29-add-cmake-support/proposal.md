# Change: Add CMake file support

## Why
Many C++ repositories use CMake rather than Visual Studio solutions/projects. Supporting CMakeLists.txt allows cppsbom to generate SBOMs for cross-platform codebases and build setups that do not include .sln/.vcxproj files.

## What Changes
- Add `--type` to select scan mode: `cmake`, `vs`, or `visualstudio` (default: Visual Studio).
- Discover `CMakeLists.txt` files reachable via `add_subdirectory` from the root `CMakeLists.txt` (including paths outside `--root`) when `--type cmake` is selected.
- Parse CMake targets defined via `add_executable` and `add_library` (including `OBJECT`, `IMPORTED`, and `INTERFACE` libraries) to identify dependencies.
- Surface CMake-derived targets in the SBOM project/dependency map using directory-qualified target identifiers.
- Log and continue on non-root CMake parse errors; root `CMakeLists.txt` parse errors are fatal.
- Update documentation/usage to mention CMake support and the new flag.

## Impact
- Affected specs: cmake-discovery (new)
- Affected code: `src/SbomTool/CommandLineOptions.cs`, `src/SbomTool/SolutionScanner.cs`, `src/SbomTool/ProjectAnalyzer.cs`, new CMake parsing/scanning components, CLI help/docs

## Acceptance Criteria
- Default scan mode remains Visual Studio; `--type cmake` switches to CMake discovery.
- Given a repo with only CMakeLists.txt and `--type cmake`, cppsbom includes CMake targets in the SBOM project map.
- Given a target with `target_include_directories` and `target_link_libraries`, the SBOM includes those linked dependencies; include directories are used for resolution, not recorded as dependencies.
- Non-root CMake parse errors are logged and do not fail the run; root `CMakeLists.txt` parse errors are fatal.
- Given `--type cmake` and no root `CMakeLists.txt`, the tool exits with a fatal error.
- CMake target identifiers are normalized to lowercase paths with `/` separators and no directory prefix for root-level targets.
- Given `--type cmake` and an unreadable root `CMakeLists.txt`, the tool exits with a fatal error.
- Linked library entries that are literal file paths are recorded as file dependencies.
- Normalized target identifier collisions cause a fatal error.
- Linked library entries ending with `.lib`, `.a`, `.dll`, `.so`, or `.dylib` are recorded as file dependencies.
- Scan modes are exclusive: Visual Studio scan ignores `CMakeLists.txt`, and CMake scan ignores `.sln`/`.vcxproj`.
