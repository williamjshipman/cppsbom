using System.Text.RegularExpressions;

namespace CppSbom;

internal sealed class SourceScanner
{
    private static readonly Regex IncludeRegex = new(@"#\s*include\s*(?<delim>[<""])(?<path>[^>""]+)[>""]", RegexOptions.Compiled);
    private static readonly Regex ImportRegex = new(@"#\s*import\s*""(?<path>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PragmaLibRegex = new(@"#\s*pragma\s+comment\s*\(\s*lib\s*,\s*""(?<lib>[^""]+)""\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GuidRegex = new(@"\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}", RegexOptions.Compiled);
    private static readonly Regex StringLiteralRegex = new(@"(?:(?:L|u|U|u8)?""(?<value>[^""\\\n]{3,})"")", RegexOptions.Compiled);

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

    private static void ScanIncludes(string file, string text, SourceScanResult result)
    {
        foreach (Match match in IncludeRegex.Matches(text))
        {
            var value = match.Groups["path"].Value.Trim();
            var isSystem = match.Groups["delim"].Value == "<";
            result.Includes.Add(new IncludeMatch(file, value, isSystem));
        }
    }

    private static void ScanImports(string file, string text, SourceScanResult result)
    {
        foreach (Match match in ImportRegex.Matches(text))
        {
            var value = match.Groups["path"].Value.Trim();
            result.Imports.Add(new DirectiveMatch(file, value));
        }
    }

    private static void ScanPragmaLibs(string file, string text, SourceScanResult result)
    {
        foreach (Match match in PragmaLibRegex.Matches(text))
        {
            var value = match.Groups["lib"].Value.Trim();
            result.PragmaLibs.Add(new DirectiveMatch(file, value));
        }
    }

    private static void ScanProgIds(string file, string text, SourceScanResult result)
    {
        foreach (Match match in StringLiteralRegex.Matches(text))
        {
            var value = match.Groups["value"].Value.Trim();
            if (LooksLikeProgId(value))
            {
                result.ProgIds.Add(new DirectiveMatch(file, value));
            }
        }
    }

    private static void ScanClsids(string file, string text, SourceScanResult result)
    {
        foreach (Match match in GuidRegex.Matches(text))
        {
            result.Clsids.Add(new DirectiveMatch(file, match.Value));
        }
    }

    private static bool LooksLikeProgId(string candidate)
    {
        if (!candidate.Contains('.') || candidate.Contains(' ') || candidate.Contains("::") || candidate.Contains('/'))
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
}

internal sealed record IncludeMatch(string FilePath, string Value, bool IsSystem);

internal sealed record DirectiveMatch(string FilePath, string Value);

internal sealed class SourceScanResult
{
    public List<IncludeMatch> Includes { get; } = new();
    public List<DirectiveMatch> Imports { get; } = new();
    public List<DirectiveMatch> PragmaLibs { get; } = new();
    public List<DirectiveMatch> ProgIds { get; } = new();
    public List<DirectiveMatch> Clsids { get; } = new();
}
