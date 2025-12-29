## 1. Implementation
- [x] 1.1 Add `--type` parsing with options `cmake`, `vs`, `visualstudio` (default: Visual Studio) and route scan mode accordingly.
- [x] 1.2 Discover `CMakeLists.txt` files reachable via `add_subdirectory` from the root `CMakeLists.txt` (including paths outside `--root`) when `--type cmake` is selected.
- [x] 1.3 Parse CMake targets defined via `add_executable`/`add_library` (including `OBJECT`, `IMPORTED`, and `INTERFACE` libraries), collecting sources and linked libraries; treat include directories as search paths only.
- [x] 1.4 Qualify CMake target identifiers with their directory to deduplicate target names.
- [x] 1.5 Log and continue on non-root CMake parse errors; root `CMakeLists.txt` parse errors are fatal.
- [x] 1.6 Integrate CMake targets into the existing dependency analysis and SBOM writer.
- [x] 1.7 Update CLI usage/help and README to mention CMake support and the new flag.
- [x] 1.8 Add regression tests for CMake parsing/analysis (create `tests/` project if needed).

## 2. Acceptance Criteria Tests
- [x] 2.1 Unit test: `--type cmake` triggers CMake discovery; default remains Visual Studio scanning.
- [x] 2.2 Unit test: CMake target discovery from `add_library`/`add_executable` with nested `add_subdirectory`.
- [x] 2.3 Unit test: `target_link_libraries` yields dependencies while `target_include_directories` only affects resolution.
- [x] 2.4 Unit test: Directory-qualified target naming; normalized identifier collisions are fatal.
- [x] 2.5 Unit test: Non-root CMake parse errors are logged and do not fail the scan.
- [x] 2.6 Unit test: `--type cmake` with no root `CMakeLists.txt` fails fast with a fatal error.
- [x] 2.7 Unit test: Target identifier normalization (lowercase, `/` separators, root-level naming).
- [x] 2.8 Unit test: Root `CMakeLists.txt` parse errors are fatal.
- [x] 2.9 Unit test: Literal link entries with path separators or `.lib`/`.a`/`.dll`/`.so`/`.dylib` extensions are recorded as file dependencies.
- [x] 2.10 Unit test: Non-literal `add_subdirectory` args are ignored with warnings.
- [x] 2.11 Unit test: Non-literal target source/link args are ignored with warnings.
- [x] 2.12 Unit test: `add_subdirectory` paths resolving outside `--root` are still included.
- [x] 2.13 Unit test: Non-existent include directories are ignored for include search paths.
- [x] 2.14 Unit test: Invalid `--type` value fails with a fatal error.
- [x] 2.15 Unit test: Include search paths are deduplicated after normalization.
- [x] 2.16 Unit test: `--third-party` roots apply to CMake scans, including paths outside `--root`.
