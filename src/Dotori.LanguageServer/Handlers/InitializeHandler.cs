using System.Text.Json;
using Dotori.LanguageServer.Protocol;

namespace Dotori.LanguageServer.Handlers;

/// <summary>
/// Handles the LSP initialize / initialized lifecycle.
/// </summary>
public static class InitializeHandler
{
    public static IReadOnlyList<string> GetWorkspaceRoots(JsonElement? parameters)
    {
        if (!parameters.HasValue || parameters.Value.ValueKind != JsonValueKind.Object)
            return [];
        var value = parameters.Value;

        var paths = new List<string>();
        if (value.TryGetProperty("workspaceFolders", out var folders) &&
            folders.ValueKind == JsonValueKind.Array)
        {
            foreach (var folder in folders.EnumerateArray())
            {
                if (folder.ValueKind == JsonValueKind.Object &&
                    folder.TryGetProperty("uri", out var uri))
                    AddFileUri(uri, paths);
            }
        }

        if (paths.Count == 0 && value.TryGetProperty("rootUri", out var rootUri))
            AddFileUri(rootUri, paths);

        if (paths.Count == 0 && value.TryGetProperty("rootPath", out var rootPath) &&
            rootPath.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(rootPath.GetString()))
        {
            try { paths.Add(Path.GetFullPath(rootPath.GetString()!)); }
            catch (ArgumentException) { }
            catch (IOException) { }
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return paths.Distinct(comparer).ToArray();
    }

    private static void AddFileUri(JsonElement element, List<string> paths)
    {
        if (element.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(element.GetString(), UriKind.Absolute, out var uri) || !uri.IsFile)
            return;

        var path = uri.LocalPath;
        if (OperatingSystem.IsWindows() && path.Length >= 3 && path[0] == '/' &&
            char.IsAsciiLetter(path[1]) && path[2] == ':')
            path = path[1..].Replace('/', '\\');

        try { paths.Add(Path.GetFullPath(path)); }
        catch (ArgumentException) { }
        catch (IOException) { }
    }

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
