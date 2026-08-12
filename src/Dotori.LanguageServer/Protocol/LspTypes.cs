using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dotori.LanguageServer.Protocol;

// ─── JSON-RPC 2.0 ─────────────────────────────────────────────────────────

public sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    // LSP spec: id is int | string | null — use JsonElement to handle all three
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("method")]
    public required string Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }
}

public sealed class JsonRpcNotification
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("method")]
    public required string Method { get; set; }

    [JsonPropertyName("params")]
    public object? Params { get; set; }
}

public sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public required string Message { get; set; }
}


// ─── LSP Core Types ───────────────────────────────────────────────────────

public sealed class LspPosition
{
    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("character")]
    public int Character { get; set; }
}

public sealed class LspRange
{
    [JsonPropertyName("start")]
    public required LspPosition Start { get; set; }

    [JsonPropertyName("end")]
    public required LspPosition End { get; set; }
}

public sealed class LspLocation
{
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }

    [JsonPropertyName("range")]
    public required LspRange Range { get; set; }
}

public sealed class LspTextDocumentIdentifier
{
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }
}

public sealed class LspVersionedTextDocumentIdentifier
{
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }

    [JsonPropertyName("version")]
    public int? Version { get; set; }
}

public sealed class LspTextDocumentItem
{
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }

    [JsonPropertyName("languageId")]
    public required string LanguageId { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

public sealed class LspTextDocumentContentChangeEvent
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

// ─── Diagnostic ───────────────────────────────────────────────────────────

public sealed class LspDiagnostic
{
    [JsonPropertyName("range")]
    public required LspRange Range { get; set; }

    [JsonPropertyName("severity")]
    public int? Severity { get; set; }  // 1=Error, 2=Warning, 3=Info, 4=Hint

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("message")]
    public required string Message { get; set; }
}

public sealed class PublishDiagnosticsParams
{
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }

    [JsonPropertyName("diagnostics")]
    public required List<LspDiagnostic> Diagnostics { get; set; }
}

// ─── Completion ───────────────────────────────────────────────────────────

public sealed class CompletionParams
{
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentIdentifier TextDocument { get; set; }

    [JsonPropertyName("position")]
    public required LspPosition Position { get; set; }
}

public sealed class LspCompletionItem
{
    [JsonPropertyName("label")]
    public required string Label { get; set; }

    [JsonPropertyName("kind")]
    public int? Kind { get; set; }  // 1=Text, 14=Keyword, 12=Value, 9=Module

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("documentation")]
    public string? Documentation { get; set; }

    [JsonPropertyName("insertText")]
    public string? InsertText { get; set; }
}

public sealed class CompletionList
{
    [JsonPropertyName("isIncomplete")]
    public bool IsIncomplete { get; set; }

    [JsonPropertyName("items")]
    public required List<LspCompletionItem> Items { get; set; }
}

// ─── Hover ────────────────────────────────────────────────────────────────

public sealed class HoverParams
{
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentIdentifier TextDocument { get; set; }

    [JsonPropertyName("position")]
    public required LspPosition Position { get; set; }
}

public sealed class LspMarkupContent
{
    [JsonPropertyName("kind")]
    public required string Kind { get; set; }  // "plaintext" or "markdown"

    [JsonPropertyName("value")]
    public required string Value { get; set; }
}

public sealed class LspHover
{
    [JsonPropertyName("contents")]
    public required LspMarkupContent Contents { get; set; }

    [JsonPropertyName("range")]
    public LspRange? Range { get; set; }
}

// ─── Definition ───────────────────────────────────────────────────────────

public sealed class DefinitionParams
{
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentIdentifier TextDocument { get; set; }

    [JsonPropertyName("position")]
    public required LspPosition Position { get; set; }
}

// ─── References / Rename / Formatting ────────────────────────────────────

public sealed class ReferenceParams
{
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentIdentifier TextDocument { get; set; }

    [JsonPropertyName("position")]
    public required LspPosition Position { get; set; }

    [JsonPropertyName("context")]
    public required ReferenceContext Context { get; set; }
}

public sealed class ReferenceContext
{
    [JsonPropertyName("includeDeclaration")]
    public bool IncludeDeclaration { get; set; }
}

public sealed class RenameParams
{
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentIdentifier TextDocument { get; set; }

    [JsonPropertyName("position")]
    public required LspPosition Position { get; set; }

    [JsonPropertyName("newName")]
    public required string NewName { get; set; }
}

public sealed class DocumentFormattingParams
{
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentIdentifier TextDocument { get; set; }

    [JsonPropertyName("options")]
    public FormattingOptions? Options { get; set; }
}

public sealed class FormattingOptions
{
    [JsonPropertyName("tabSize")]
    public int TabSize { get; set; }

    [JsonPropertyName("insertSpaces")]
    public bool InsertSpaces { get; set; }
}

public sealed class LspTextEdit
{
    [JsonPropertyName("range")]
    public required LspRange Range { get; set; }

    [JsonPropertyName("newText")]
    public required string NewText { get; set; }
}

public sealed class LspWorkspaceEdit
{
    [JsonPropertyName("changes")]
    public required Dictionary<string, List<LspTextEdit>> Changes { get; set; }
}

public sealed class RenameOptions
{
    [JsonPropertyName("prepareProvider")]
    public bool PrepareProvider { get; set; }
}

// ─── Initialize ───────────────────────────────────────────────────────────

public sealed class InitializeParams
{
    [JsonPropertyName("processId")]
    public int? ProcessId { get; set; }

    [JsonPropertyName("rootUri")]
    public string? RootUri { get; set; }

    [JsonPropertyName("rootPath")]
    public string? RootPath { get; set; }
}

public sealed class InitializeResult
{
    [JsonPropertyName("capabilities")]
    public required ServerCapabilities Capabilities { get; set; }

    [JsonPropertyName("serverInfo")]
    public ServerInfo? ServerInfo { get; set; }
}

public sealed class ServerCapabilities
{
    [JsonPropertyName("textDocumentSync")]
    public int TextDocumentSync { get; set; } = 1; // Full = 1

    [JsonPropertyName("completionProvider")]
    public CompletionOptions? CompletionProvider { get; set; }

    [JsonPropertyName("hoverProvider")]
    public bool HoverProvider { get; set; }

    [JsonPropertyName("definitionProvider")]
    public bool DefinitionProvider { get; set; }

    [JsonPropertyName("referencesProvider")]
    public bool ReferencesProvider { get; set; }

    [JsonPropertyName("renameProvider")]
    public RenameOptions? RenameProvider { get; set; }

    [JsonPropertyName("documentFormattingProvider")]
    public bool DocumentFormattingProvider { get; set; }
}

public sealed class CompletionOptions
{
    [JsonPropertyName("triggerCharacters")]
    public List<string>? TriggerCharacters { get; set; }
}

public sealed class ServerInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

// ─── textDocument notifications ──────────────────────────────────────────

public sealed class DidOpenTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentItem TextDocument { get; set; }
}

public sealed class DidChangeTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public required LspVersionedTextDocumentIdentifier TextDocument { get; set; }

    [JsonPropertyName("contentChanges")]
    public required List<LspTextDocumentContentChangeEvent> ContentChanges { get; set; }
}

public sealed class DidCloseTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentIdentifier TextDocument { get; set; }
}
