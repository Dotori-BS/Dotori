using Dotori.Core.Parsing;
using Dotori.LanguageServer.Protocol;

namespace Dotori.LanguageServer.Providers;

/// <summary>Formats an open Dotori document using the canonical AST formatter.</summary>
public static class FormattingProvider
{
    public static List<LspTextEdit> Format(string text, string? localFilePath)
    {
        DotoriFile parsed;
        try
        {
            parsed = DotoriParser.ParseSource(text, localFilePath ?? "<lsp>");
        }
        catch (Exception ex) when (ex is ParseException or LexerException)
        {
            return [];
        }

        var formatted = NormalizeLineEndings(DotoriFormatter.Format(parsed));
        var normalized = NormalizeLineEndings(text);
        if (formatted == normalized) return [];

        var lines = normalized.Split('\n');
        var endLine = Math.Max(0, lines.Length - 1);
        var endCharacter = lines[endLine].Length;
        return
        [
            new LspTextEdit
            {
                Range = new LspRange
                {
                    Start = new LspPosition { Line = 0, Character = 0 },
                    End = new LspPosition { Line = endLine, Character = endCharacter },
                },
                NewText = formatted,
            },
        ];
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n');
}
