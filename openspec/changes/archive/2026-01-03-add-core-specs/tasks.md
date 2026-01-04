## 1. Implementation
- [x] 1.1 Verify CLI parsing behavior against `src/SbomTool/CommandLineOptions.cs` and update specs if needed.
- [x] 1.2 Verify Visual Studio discovery and dependency classification behavior against `src/SbomTool/SolutionScanner.cs`, `src/SbomTool/ProjectAnalyzer.cs`, `src/SbomTool/SourceScanner.cs`, and `src/SbomTool/CMakeTargetAnalyzer.cs`.
- [x] 1.3 Verify SBOM output and logging behavior against `src/SbomTool/SbomGenerator.cs`, `src/SbomTool/SbomWriter.cs`, `src/SbomTool/Program.cs`, and `src/SbomTool/ComRegistryResolver.cs`.
- [x] 1.4 Run `openspec validate add-core-specs --strict`.
- [ ] 1.5 After approval, archive the change (`openspec archive add-core-specs --yes`) and confirm `openspec validate --strict`.
