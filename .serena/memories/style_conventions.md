# Style & conventions

## C# (observed)
- 4-space indentation.
- File-scoped namespace (`namespace CppSbom;`).
- PascalCase for types/methods; camelCase for locals/parameters; `var` used for locals.
- Nullable reference types enabled (`<Nullable>enable</Nullable>`).

## C++ conventions (from `AGENTS.md`, may be legacy)
- C++17, 4-space indentation.
- PascalCase classes/structs; lower_snake_case free functions; UPPER_SNAKE_CASE constants.
- Public headers in `include/cppsbom/`, implementations in `src/`, shared utilities in `src/common/`.
- Prefer `std::unique_ptr`/`std::shared_ptr` over raw owning pointers.

## TODO
- Confirm whether C++ guidelines still apply or are legacy now that the repo is a .NET project.