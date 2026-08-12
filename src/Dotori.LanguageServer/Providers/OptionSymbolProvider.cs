using System.Text.RegularExpressions;
using Dotori.Core.Parsing;
using Dotori.LanguageServer.Protocol;

namespace Dotori.LanguageServer.Providers;

/// <summary>Finds references to declared Dotori build options and creates safe rename edits.</summary>
public static partial class OptionSymbolProvider
{
    public static LspLocation[] GetReferences(
        string text, string uri, int line, int character, bool includeDeclaration)
    {
        var occurrences = FindOccurrences(text);
        var symbol = DeclaredSymbolAt(occurrences, line, character);
        if (symbol is null) return [];

        return occurrences
            .Where(item => item.Name == symbol && (includeDeclaration || !item.Declaration))
            .Select(item => new LspLocation { Uri = uri, Range = item.Range })
            .ToArray();
    }

    public static LspRange? PrepareRename(string text, int line, int character)
    {
        var occurrences = FindOccurrences(text);
        var symbol = DeclaredSymbolAt(occurrences, line, character);
        return symbol is null
            ? null
            : occurrences.First(item => item.Name == symbol && Contains(item.Range, line, character)).Range;
    }

    public static LspWorkspaceEdit? Rename(
        string text, string uri, int line, int character, string newName)
    {
        if (!ValidIdentifierRegex().IsMatch(newName)) return null;

        var occurrences = FindOccurrences(text);
        var symbol = DeclaredSymbolAt(occurrences, line, character);
        if (symbol is null) return null;

        var edits = occurrences
            .Where(item => item.Name == symbol)
            .Select(item => new LspTextEdit { Range = item.Range, NewText = newName })
            .ToList();
        return new LspWorkspaceEdit
        {
            Changes = new Dictionary<string, List<LspTextEdit>>(StringComparer.OrdinalIgnoreCase)
            {
                [uri] = edits,
            },
        };
    }

    private static List<Occurrence> FindOccurrences(string text)
    {
        List<Token> tokens;
        try
        {
            tokens = new Lexer(text, "<lsp>").Tokenize();
        }
        catch (LexerException)
        {
            return [];
        }

        var result = new List<Occurrence>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (Is(tokens, i, TokenKind.Ident, "option") &&
                Is(tokens, i + 1, TokenKind.Ident) && Is(tokens, i + 2, TokenKind.LBrace))
            {
                result.Add(FromToken(tokens[i + 1], declaration: true));
                i += 2;
                continue;
            }

            if (Is(tokens, i, TokenKind.LBracket) &&
                Is(tokens, i + 1, TokenKind.Ident) && Is(tokens, i + 2, TokenKind.RBracket))
            {
                result.Add(FromToken(tokens[i + 1], declaration: false));
                i += 2;
                continue;
            }

            if (!Is(tokens, i, TokenKind.Ident, "option") || !Is(tokens, i + 1, TokenKind.Equals))
                continue;

            if (Is(tokens, i + 2, TokenKind.String))
            {
                result.Add(FromToken(tokens[i + 2], declaration: false));
                i += 2;
            }
            else if (Is(tokens, i + 2, TokenKind.LBrace))
            {
                for (var j = i + 3; j < tokens.Count && tokens[j].Kind != TokenKind.RBrace; j++)
                    if (tokens[j].Kind == TokenKind.String)
                        result.Add(FromToken(tokens[j], declaration: false));
            }
        }
        return result;
    }

    private static string? DeclaredSymbolAt(
        IReadOnlyList<Occurrence> occurrences, int line, int character)
    {
        var selected = occurrences.FirstOrDefault(item => Contains(item.Range, line, character));
        if (selected is null) return null;
        return occurrences.Any(item => item.Declaration && item.Name == selected.Name)
            ? selected.Name
            : null;
    }

    private static bool Contains(LspRange range, int line, int character) =>
        range.Start.Line == line && character >= range.Start.Character && character <= range.End.Character;

    private static bool Is(IReadOnlyList<Token> tokens, int index, TokenKind kind, string? text = null) =>
        index >= 0 && index < tokens.Count && tokens[index].Kind == kind &&
        (text is null || tokens[index].Text == text);

    private static Occurrence FromToken(Token token, bool declaration)
    {
        var startCharacter = token.Location.Column - 1 + (token.Kind == TokenKind.String ? 1 : 0);
        return new Occurrence(
            token.Text,
            new LspRange
            {
                Start = new LspPosition { Line = token.Location.Line - 1, Character = startCharacter },
                End = new LspPosition
                {
                    Line = token.Location.Line - 1,
                    Character = startCharacter + token.Text.Length,
                },
            },
            declaration);
    }

    private sealed record Occurrence(string Name, LspRange Range, bool Declaration);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_-]*$")]
    private static partial Regex ValidIdentifierRegex();
}
