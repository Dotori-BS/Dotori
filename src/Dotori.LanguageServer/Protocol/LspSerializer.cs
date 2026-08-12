using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Dotori.LanguageServer.Protocol;

/// <summary>
/// Source-generated JSON serialization context for all LSP types.
/// Required for NativeAOT compatibility (no reflection).
/// </summary>
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcResponse))]
[JsonSerializable(typeof(JsonRpcNotification))]
[JsonSerializable(typeof(JsonRpcError))]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(ServerCapabilities))]
[JsonSerializable(typeof(ServerInfo))]
[JsonSerializable(typeof(CompletionOptions))]
[JsonSerializable(typeof(DidOpenTextDocumentParams))]
[JsonSerializable(typeof(DidChangeTextDocumentParams))]
[JsonSerializable(typeof(DidCloseTextDocumentParams))]
[JsonSerializable(typeof(CompletionParams))]
[JsonSerializable(typeof(CompletionList))]
[JsonSerializable(typeof(LspCompletionItem))]
[JsonSerializable(typeof(List<LspCompletionItem>))]
[JsonSerializable(typeof(HoverParams))]
[JsonSerializable(typeof(LspHover))]
[JsonSerializable(typeof(LspMarkupContent))]
[JsonSerializable(typeof(DefinitionParams))]
[JsonSerializable(typeof(ReferenceParams))]
[JsonSerializable(typeof(ReferenceContext))]
[JsonSerializable(typeof(RenameParams))]
[JsonSerializable(typeof(DocumentFormattingParams))]
[JsonSerializable(typeof(FormattingOptions))]
[JsonSerializable(typeof(RenameOptions))]
[JsonSerializable(typeof(LspTextEdit))]
[JsonSerializable(typeof(List<LspTextEdit>))]
[JsonSerializable(typeof(LspWorkspaceEdit))]
[JsonSerializable(typeof(Dictionary<string, List<LspTextEdit>>))]
[JsonSerializable(typeof(LspLocation))]
[JsonSerializable(typeof(LspLocation[]))]
[JsonSerializable(typeof(PublishDiagnosticsParams))]
[JsonSerializable(typeof(LspDiagnostic))]
[JsonSerializable(typeof(List<LspDiagnostic>))]
[JsonSerializable(typeof(LspPosition))]
[JsonSerializable(typeof(LspRange))]
[JsonSerializable(typeof(LspTextDocumentIdentifier))]
[JsonSerializable(typeof(LspTextDocumentItem))]
[JsonSerializable(typeof(LspVersionedTextDocumentIdentifier))]
[JsonSerializable(typeof(LspTextDocumentContentChangeEvent))]
[JsonSerializable(typeof(List<LspTextDocumentContentChangeEvent>))]
[JsonSerializable(typeof(object))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class LspJsonContext : JsonSerializerContext
{
    /// <summary>Singleton options for deserialization.</summary>
    public static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        TypeInfoResolver = Default,
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>
/// Central serialization helper.
/// </summary>
public static class LspSerializer
{
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        TypeInfoResolver = LspJsonContext.Default,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value) where T : notnull
    {
        var typeInfo = (JsonTypeInfo<T>)LspJsonContext.Default.GetTypeInfo(typeof(T))!;
        return JsonSerializer.Serialize(value, typeInfo);
    }

    public static T? Deserialize<T>(string json) where T : class
    {
        var typeInfo = (JsonTypeInfo<T>)LspJsonContext.Default.GetTypeInfo(typeof(T))!;
        return JsonSerializer.Deserialize(json, typeInfo);
    }

    public static T? DeserializeElement<T>(System.Text.Json.JsonElement element) where T : class
    {
        var typeInfo = (JsonTypeInfo<T>)LspJsonContext.Default.GetTypeInfo(typeof(T))!;
        return element.Deserialize(typeInfo);
    }

    public static JsonRpcRequest? DeserializeRequest(string json)
    {
        return JsonSerializer.Deserialize(json, LspJsonContext.Default.JsonRpcRequest);
    }

    public static string SerializeResponse(JsonRpcResponse response)
    {
        return JsonSerializer.Serialize(response, LspJsonContext.Default.JsonRpcResponse);
    }

    public static string SerializeNotification(JsonRpcNotification notification)
    {
        return JsonSerializer.Serialize(notification, LspJsonContext.Default.JsonRpcNotification);
    }
}
