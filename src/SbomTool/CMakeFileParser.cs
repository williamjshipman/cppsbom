using System.Text;

namespace CppSbom;

/// <summary>
/// Represents a parsed CMake command entry.
/// </summary>
/// <param name="Name">Command name.</param>
/// <param name="DirectoryPath">Directory containing the command.</param>
/// <param name="Arguments">Command arguments.</param>
internal sealed record CMakeCommand(string Name, string DirectoryPath, IReadOnlyList<string> Arguments);

/// <summary>
/// Represents the result of parsing a CMake file.
/// </summary>
/// <param name="Commands">Parsed commands.</param>
/// <param name="HasError">True when a parse error occurred.</param>
/// <param name="ErrorMessage">Parse error message.</param>
internal sealed record CMakeFileParseResult(IReadOnlyList<CMakeCommand> Commands, bool HasError, string? ErrorMessage);

/// <summary>
/// Parses CMakeLists.txt files into commands.
/// </summary>
internal sealed class CMakeFileParser
{
    /// <summary>
    /// Initializes a new parser instance.
    /// </summary>
    public CMakeFileParser()
    {
    }

    /// <summary>
    /// Parses a CMake file into commands and error status.
    /// </summary>
    /// <param name="path">Path to the CMakeLists.txt file.</param>
    /// <returns>Parse result with commands and error state.</returns>
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

    /// <summary>
    /// Parses command arguments within parentheses.
    /// </summary>
    /// <param name="text">Full file text.</param>
    /// <param name="index">Current index within text.</param>
    /// <param name="args">Parsed arguments.</param>
    /// <param name="error">Error message if parsing fails.</param>
    /// <returns>True when arguments were parsed successfully.</returns>
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

    /// <summary>
    /// Adds a token to the argument list, splitting on semicolons.
    /// </summary>
    /// <param name="args">Argument list to populate.</param>
    /// <param name="token">Token buffer.</param>
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

    /// <summary>
    /// Advances the index past whitespace and comment lines.
    /// </summary>
    /// <param name="text">Full file text.</param>
    /// <param name="index">Index to advance.</param>
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

    /// <summary>
    /// Advances the index to the end of the current line.
    /// </summary>
    /// <param name="text">Full file text.</param>
    /// <param name="index">Index to advance.</param>
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

    /// <summary>
    /// Determines whether a character can start a command identifier.
    /// </summary>
    /// <param name="ch">Character to inspect.</param>
    /// <returns>True when the character starts an identifier.</returns>
    private static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch == '_';

    /// <summary>
    /// Determines whether a character can be part of a command identifier.
    /// </summary>
    /// <param name="ch">Character to inspect.</param>
    /// <returns>True when the character is a valid identifier part.</returns>
    private static bool IsIdentifierPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
}
