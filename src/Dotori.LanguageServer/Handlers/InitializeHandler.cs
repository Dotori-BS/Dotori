using Dotori.LanguageServer.Protocol;

namespace Dotori.LanguageServer.Handlers;

/// <summary>
/// Handles the LSP initialize / initialized lifecycle.
/// </summary>
public static class InitializeHandler
{
    public static InitializeResult Handle()
    {
        return new InitializeResult
        {
            ServerInfo = new ServerInfo
            {
                Name    = "dotori-lsp",
                Version = "1.0.0",
            },
            Capabilities = new ServerCapabilities
            {
                TextDocumentSync = 1, // Full sync
                HoverProvider    = true,
                DefinitionProvider = true,
                ReferencesProvider = true,
                RenameProvider = new RenameOptions { PrepareProvider = true },
                DocumentFormattingProvider = true,
                CompletionProvider = new CompletionOptions
                {
                    TriggerCharacters = ["=", "[", " "],
                },
            },
        };
    }
}
