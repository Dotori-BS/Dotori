using Dotori.Core.Parsing;
using Dotori.LanguageServer.Protocol;

namespace Dotori.LanguageServer.Providers;

/// <summary>Resolves dependency path strings to their .dotori definitions.</summary>
public static class DefinitionProvider
{
    public static LspLocation? GetDefinition(string text, int line, int character, string? localFilePath)
    {
        if (localFilePath is null || line < 0 || character < 0) return null;
        var lines = text.Split('\n');
        if (line >= lines.Length || character > lines[line].Length) return null;

        Token[] tokens;
        try
        {
            tokens = new Lexer(string.Join('\n', lines.Take(line + 1)), localFilePath)
                .Tokenize().Where(token => token.Kind != TokenKind.Comment).ToArray();
        }
        catch (LexerException)
        {
            return null;
        }

        // Track lexical scopes so nested dependency objects, inline blocks, comments,
        // and braces inside strings do not confuse dependency-path detection.
        var scopes = new Stack<bool>();
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.Kind == TokenKind.LBrace)
            {
                var isDependencies = index > 0 && tokens[index - 1].Kind == TokenKind.Ident &&
                                     tokens[index - 1].Text == "dependencies";
                scopes.Push(isDependencies || (scopes.TryPeek(out var inside) && inside));
            }
            else if (token.Kind == TokenKind.RBrace)
            {
                if (scopes.Count > 0) scopes.Pop();
            }
            else if (token.Kind == TokenKind.String && token.Location.Line - 1 == line &&
                     index >= 2 && tokens[index - 1].Kind == TokenKind.Equals &&
                     tokens[index - 2].Kind == TokenKind.Ident && tokens[index - 2].Text == "path" &&
                     scopes.TryPeek(out var inside) && inside)
            {
                var start = token.Location.Column - 1;
                var end = FindStringEnd(lines[line], start);
                if (character < start || character > end) continue;

                string definition;
                try
                {
                    var projectDirectory = Path.GetDirectoryName(localFilePath) ?? ".";
                    definition = Path.Combine(Path.GetFullPath(Path.Combine(projectDirectory, token.Text)), ".dotori");
                }
                catch (ArgumentException)
                {
                    return null;
                }
                if (!File.Exists(definition)) return null;
                return new LspLocation
                {
                    Uri = new Uri(definition).AbsoluteUri,
                    Range = new LspRange
                    {
                        Start = new LspPosition { Line = 0, Character = 0 },
                        End = new LspPosition { Line = 0, Character = 0 },
                    },
                };
            }
        }
        return null;
    }

    private static int FindStringEnd(string line, int start)
    {
        for (var index = start + 1; index < line.Length; index++)
        {
            if (line[index] == '\\') index++;
            else if (line[index] == '"') return index;
        }
        return -1;
    }
}
