namespace Dotori.LanguageServer;

/// <summary>
/// In-memory store for currently open text documents.
/// </summary>
public sealed class DocumentStore
{
    private readonly Dictionary<string, DocumentEntry> _docs =
        new(StringComparer.OrdinalIgnoreCase);

    public void Open(string uri, string text, int version)
    {
        _docs[uri] = new DocumentEntry(uri, text, version);
    }

    public void Update(string uri, string text, int? version)
    {
        if (_docs.TryGetValue(uri, out var existing))
            _docs[uri] = existing with { Text = text, Version = version ?? existing.Version };
        else
            _docs[uri] = new DocumentEntry(uri, text, version ?? 0);
    }

    public void Close(string uri)
    {
        _docs.Remove(uri);
    }

    public DocumentEntry? Get(string uri) =>
        _docs.TryGetValue(uri, out var doc) ? doc : null;

    public IReadOnlyCollection<string> OpenUris => _docs.Keys;
}

public sealed record DocumentEntry(string Uri, string Text, int Version)
{
    /// <summary>
    /// Convert LSP URI (file:///path) to a local file system path.
    /// </summary>
    public string? LocalPath
    {
        get
        {
            if (!System.Uri.TryCreate(Uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
                return null;

            var path = parsed.LocalPath;
            // VS Code escapes the drive colon (file:///c%3A/...). System.Uri
            // decodes it but leaves a leading slash instead of recognizing a DOS path.
            if (OperatingSystem.IsWindows() && path.Length >= 3 && path[0] == '/' &&
                char.IsAsciiLetter(path[1]) && path[2] == ':')
                return path[1..].Replace('/', '\\');
            return path;
        }
    }
}
