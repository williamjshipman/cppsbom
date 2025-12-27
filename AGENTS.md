# Repository Guidelines

## Project Structure & Module Organization
Keep executable code in `src/` with one logical component per translation unit, and mirror public headers in `include/cppsbom/`. Shared utilities that cross modules sit under `src/common/`. Place tests in `tests/` using the same relative paths as their targets (for example, `tests/common/graph_parser_test.cpp`). If you add data fixtures or SBOM samples, store them in `tests/fixtures/` so they remain version-controlled.

## Build, Test, and Development Commands
- `cmake -S . -B build -DCPPSBOM_ENABLE_TESTS=ON` configures the project with tests enabled.
- `cmake --build build` compiles all targets and regenerates the SBOM CLI.
- `ctest --test-dir build` runs the full unit-test suite.
- `cmake --build build --target clang-format` applies the formatting presets when available.

## Coding Style & Naming Conventions
Use C++17, four-space indentation, and keep each file focused on a single responsibility. Name classes and structs in PascalCase (`SbomDocument`), free functions in lower_snake_case, and constants in UPPER_SNAKE_CASE. Prefer `.hpp` headers for public interfaces and `.cpp` sources for implementations. Run `clang-format` before committing, and favor `std::unique_ptr`/`std::shared_ptr` over raw owning pointers.

## Testing Guidelines
Write unit tests with GoogleTest; give each test file a `_test.cpp` suffix and group scenarios via the component name (e.g., `SBOMParser.BasicRoundTrip`). Ensure new features include regression coverage and that negative-path tests accompany validation logic. If integration or fixture-heavy scenarios are required, document the dataset provenance in a comment at the top of the test file. Aim to keep coverage stable or trending upward; add `// TODO(you):` tags for deliberate gaps.

## Commit & Pull Request Guidelines
Craft commit summaries in the imperative mood (50 characters or fewer) and include a short body when behavior changes or dependencies shift. Prefer Conventional Commit prefixes (`feat:`, `fix:`, `docs:`) to aid changelog generation. Pull requests should describe the problem, the solution, and validation steps (`ctest` output or reproducible commands). Link issues when relevant, note schema or CLI flag changes explicitly, and attach screenshots for UI or report-format updates.

## Security & SBOM Hygiene
Never check in proprietary SBOM inputs; redact sensitive metadata before committing. Validate exported SBOMs against the SPDX schema using your preferred validator, and capture anomalies in the pull request description. Audit third-party dependencies quarterly by regenerating the SBOM and documenting version deltas.
