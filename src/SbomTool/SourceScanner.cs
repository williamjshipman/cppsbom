using System.Text.RegularExpressions;

namespace CppSbom;

/// <summary>
/// Scans source files for include, import, pragma, and COM references.
/// </summary>
internal sealed class SourceScanner
{
    /// <summary>
    /// Regex for C/C++ include directives.
    /// </summary>
    private static readonly Regex IncludeRegex = new(@"#\s*include\s*(?<delim>[<""])(?<path>[^>""]+)[>""]", RegexOptions.Compiled);
    /// <summary>
    /// Regex for C++20 import directives.
    /// </summary>
    private static readonly Regex ImportRegex = new(@"#\s*import\s*""(?<path>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>
    /// Regex for pragma comment lib directives.
    /// </summary>
    private static readonly Regex PragmaLibRegex = new(@"#\s*pragma\s+comment\s*\(\s*lib\s*,\s*""(?<lib>[^""]+)""\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>
    /// Regex for GUID literals.
    /// </summary>
    private static readonly Regex GuidRegex = new(@"\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}", RegexOptions.Compiled);
    /// <summary>
    /// Regex for string literals used to detect ProgIDs.
    /// </summary>
    private static readonly Regex StringLiteralRegex = new(@"(?:(?:L|u|U|u8)?""(?<value>[^""\\\n]{3,})"")", RegexOptions.Compiled);
    /// <summary>
    /// Extensions that disqualify ProgID candidates.
    /// </summary>
    private static readonly HashSet<string> DisallowedProgIdExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".h",
        ".hh",
        ".hpp",
        ".hxx",
        ".inc",
        ".idl",
        ".tlb",
        ".tlh",
        ".tli",
        ".def",
        ".rc",
        ".manifest"
    };

    /// <summary>
    /// Scans a set of files and returns matched directives.
    /// </summary>
    /// <param name="files">Files to scan.</param>
    /// <returns>Scan result with collected directives.</returns>
    public SourceScanResult Scan(IEnumerable<string> files)
    {
        var result = new SourceScanResult();
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            ScanIncludes(file, text, result);
            ScanImports(file, text, result);
            ScanPragmaLibs(file, text, result);
            ScanProgIds(file, text, result);
            ScanClsids(file, text, result);
        }

        return result;
    }

    /// <summary>
    /// Scans include directives in file text.
    /// </summary>
    /// <param name="file">Source file path.</param>
    /// <param name="text">File contents.</param>
    /// <param name="result">Result collection to populate.</param>
    private static void ScanIncludes(string file, string text, SourceScanResult result)
    {
        foreach (Match match in IncludeRegex.Matches(text))
        {
            var value = match.Groups["path"].Value.Trim();
            var isSystem = match.Groups["delim"].Value == "<";
            result.Includes.Add(new IncludeMatch(file, value, isSystem));
        }
    }

    /// <summary>
    /// Scans import directives in file text.
    /// </summary>
    /// <param name="file">Source file path.</param>
    /// <param name="text">File contents.</param>
    /// <param name="result">Result collection to populate.</param>
    private static void ScanImports(string file, string text, SourceScanResult result)
    {
        foreach (Match match in ImportRegex.Matches(text))
        {
            var value = match.Groups["path"].Value.Trim();
            result.Imports.Add(new DirectiveMatch(file, value));
        }
    }

    /// <summary>
    /// Scans pragma comment lib directives in file text.
    /// </summary>
    /// <param name="file">Source file path.</param>
    /// <param name="text">File contents.</param>
    /// <param name="result">Result collection to populate.</param>
    private static void ScanPragmaLibs(string file, string text, SourceScanResult result)
    {
        foreach (Match match in PragmaLibRegex.Matches(text))
        {
            var value = match.Groups["lib"].Value.Trim();
            result.PragmaLibs.Add(new DirectiveMatch(file, value));
        }
    }

    /// <summary>
    /// Scans ProgID-like string literals in file text.
    /// </summary>
    /// <param name="file">Source file path.</param>
    /// <param name="text">File contents.</param>
    /// <param name="result">Result collection to populate.</param>
    private static void ScanProgIds(string file, string text, SourceScanResult result)
    {
        foreach (Match match in StringLiteralRegex.Matches(text))
        {
            if (IsInPreprocessorDirective(text, match))
            {
                continue;
            }

            var value = match.Groups["value"].Value.Trim();
            if (LooksLikeProgId(value))
            {
                result.ProgIds.Add(new DirectiveMatch(file, value));
            }
        }
    }

    /// <summary>
    /// Scans CLSID GUID literals in file text.
    /// </summary>
    /// <param name="file">Source file path.</param>
    /// <param name="text">File contents.</param>
    /// <param name="result">Result collection to populate.</param>
    private static void ScanClsids(string file, string text, SourceScanResult result)
    {
        foreach (Match match in GuidRegex.Matches(text))
        {
            result.Clsids.Add(new DirectiveMatch(file, match.Value));
        }
    }

    /// <summary>
    /// Determines whether a string literal matches a ProgID pattern.
    /// </summary>
    /// <param name="candidate">Candidate literal string.</param>
    /// <returns>True when the literal resembles a ProgID.</returns>
    private static bool LooksLikeProgId(string candidate)
    {
        if (!candidate.Contains('.') || candidate.Contains(' ') || candidate.Contains("::") || candidate.Contains('/'))
        {
            return false;
        }

        var extension = Path.GetExtension(candidate);
        if (!string.IsNullOrEmpty(extension) && DisallowedProgIdExtensions.Contains(extension))
        {
            return false;
        }

        var parts = candidate.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                return false;
            }

            foreach (var ch in part)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')
                {
                    return false;
                }
            }
        }

        return char.IsLetter(candidate[0]);
    }

    /// <summary>
    /// Checks whether a match occurs within a preprocessor directive.
    /// </summary>
    /// <param name="text">Full file contents.</param>
    /// <param name="match">Match to evaluate.</param>
    /// <returns>True when the match is in a directive line.</returns>
    private static bool IsInPreprocessorDirective(string text, Match match)
    {
        var lineStart = text.LastIndexOf('\n', match.Index);
        lineStart = lineStart == -1 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', match.Index);
        lineEnd = lineEnd == -1 ? text.Length : lineEnd;

        var line = text.Substring(lineStart, lineEnd - lineStart).TrimStart();
        if (!line.StartsWith('#'))
        {
            return false;
        }

        line = line.Substring(1).TrimStart();
        return line.StartsWith("include", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("import", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("pragma", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Represents a parsed include directive.
/// </summary>
/// <param name="FilePath">File that contains the include.</param>
/// <param name="Value">Include value.</param>
/// <param name="IsSystem">True when the include used angle brackets.</param>
internal sealed record IncludeMatch(string FilePath, string Value, bool IsSystem);

/// <summary>
/// Represents a parsed directive match.
/// </summary>
/// <param name="FilePath">File that contains the directive.</param>
/// <param name="Value">Directive value.</param>
internal sealed record DirectiveMatch(string FilePath, string Value);

/// <summary>
/// Aggregates directive matches discovered during scanning.
/// </summary>
internal sealed class SourceScanResult
{
    /// <summary>
    /// Gets include directives found in scanned files.
    /// </summary>
    public List<IncludeMatch> Includes { get; } = new();
    /// <summary>
    /// Gets import directives found in scanned files.
    /// </summary>
    public List<DirectiveMatch> Imports { get; } = new();
    /// <summary>
    /// Gets pragma comment lib directives found in scanned files.
    /// </summary>
    public List<DirectiveMatch> PragmaLibs { get; } = new();
    /// <summary>
    /// Gets ProgID matches found in scanned files.
    /// </summary>
    public List<DirectiveMatch> ProgIds { get; } = new();
    /// <summary>
    /// Gets CLSID matches found in scanned files.
    /// </summary>
    public List<DirectiveMatch> Clsids { get; } = new();
}
