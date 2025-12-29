using System.Text;

namespace CppSbom;

internal sealed record CMakeCommand(string Name, string DirectoryPath, IReadOnlyList<string> Arguments);

internal sealed record CMakeFileParseResult(IReadOnlyList<CMakeCommand> Commands, bool HasError, string? ErrorMessage);

internal sealed class CMakeFileParser
{
    public CMakeFileParser()
    {
    }

    public CMakeFileParseResult ParseFile(string path)
    {
        var commands = new List<CMakeCommand>();
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return new CMakeFileParseResult(commands, true, $"Unable to read '{path}': {ex.Message}");
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var index = 0;
        while (index < text.Length)
        {
            SkipWhitespaceAndComments(text, ref index);
            if (index >= text.Length)
            {
                break;
            }

            if (!IsIdentifierStart(text[index]))
            {
                index++;
                continue;
            }

            var nameStart = index;
            while (index < text.Length && IsIdentifierPart(text[index]))
            {
                index++;
            }

            var name = text[nameStart..index];
            var lookahead = index;
            SkipWhitespaceAndComments(text, ref lookahead);
            if (lookahead >= text.Length || text[lookahead] != '(')
            {
                index = lookahead;
                continue;
            }

            index = lookahead + 1;
            if (!TryParseArguments(text, ref index, out var args, out var error))
            {
                return new CMakeFileParseResult(commands, true, $"{error} in '{path}'");
            }

            commands.Add(new CMakeCommand(name, directory, args));
        }

        return new CMakeFileParseResult(commands, false, null);
    }

    private static bool TryParseArguments(string text, ref int index, out List<string> args, out string? error)
    {
        args = new List<string>();
        error = null;
        var token = new StringBuilder();
        var depth = 1;
        var inQuote = false;
        while (index < text.Length)
        {
            var ch = text[index];

            if (inQuote)
            {
                if (ch == '"')
                {
                    inQuote = false;
                }
                else
                {
                    token.Append(ch);
                }

                index++;
                continue;
            }

            if (ch == '"')
            {
                inQuote = true;
                index++;
                continue;
            }

            if (ch == '#')
            {
                SkipLine(text, ref index);
                continue;
            }

            if (ch == '(')
            {
                depth++;
                token.Append(ch);
                index++;
                continue;
            }

            if (ch == ')')
            {
                depth--;
                if (depth == 0)
                {
                    AddToken(args, token);
                    index++;
                    return true;
                }

                token.Append(ch);
                index++;
                continue;
            }

            if (depth == 1 && char.IsWhiteSpace(ch))
            {
                AddToken(args, token);
                index++;
                continue;
            }

            token.Append(ch);
            index++;
        }

        error = "Unbalanced parentheses";
        return false;
    }

    private static void AddToken(ICollection<string> args, StringBuilder token)
    {
        if (token.Length == 0)
        {
            return;
        }

        var raw = token.ToString();
        token.Clear();
        foreach (var piece in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            args.Add(piece);
        }
    }

    private static void SkipWhitespaceAndComments(string text, ref int index)
    {
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            if (text[index] == '#')
            {
                SkipLine(text, ref index);
                continue;
            }

            break;
        }
    }

    private static void SkipLine(string text, ref int index)
    {
        while (index < text.Length && text[index] != '\n')
        {
            index++;
        }

        if (index < text.Length)
        {
            index++;
        }
    }

    private static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch == '_';

    private static bool IsIdentifierPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
}
