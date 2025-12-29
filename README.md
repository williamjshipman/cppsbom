# cppsbom
C++ Software Bill of Materials

## Output formats
The CLI emits SPDX JSON by default. Use `--format` to switch:

```powershell
dotnet run --project Project.vcxproj -- --format spdx
dotnet run --project Project.vcxproj -- --format cyclonedx
```

## Scan modes
Visual Studio scanning is the default. Use `--type cmake` to scan CMake projects:

```powershell
dotnet run --project Project.vcxproj -- --type cmake
```
