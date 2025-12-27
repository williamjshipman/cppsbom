# Suggested commands (Windows / PowerShell)

## .NET (current repo)
- Build: `dotnet build cppsbom.sln`
- Run CLI: `dotnet run --project src/SbomTool/SbomTool.csproj -- --help`
- Example: `dotnet run --project src/SbomTool/SbomTool.csproj -- --root <path> --output <file>`

## C++/CMake (from `AGENTS.md`, may be legacy)
- Configure: `cmake -S . -B build -DCPPSBOM_ENABLE_TESTS=ON`
- Build: `cmake --build build`
- Test: `ctest --test-dir build`
- Format: `cmake --build build --target clang-format`

## Repo utilities
- List files: `Get-ChildItem -Force`
- Recursive list: `Get-ChildItem -Recurse`
- Search text (fast): `rg "pattern"` or `rg --files`
- Git status: `git status -sb`