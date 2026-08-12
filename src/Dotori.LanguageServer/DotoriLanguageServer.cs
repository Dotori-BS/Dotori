using System.Text.Json;
using Dotori.LanguageServer.Handlers;
using Dotori.LanguageServer.Protocol;
using Dotori.LanguageServer.Providers;
using Dotori.LanguageServer.Transport;

namespace Dotori.LanguageServer;

/// <summary>
/// Main entry point for the Dotori Language Server.
/// Implements LSP 3.17 over stdio using manual JSON-RPC 2.0 framing.
/// </summary>
public static class DotoriLanguageServer
{
    private static TextWriter? _log;

    /// <summary>
    /// Start the language server and process messages until the client disconnects or sends shutdown.
    /// </summary>
    public static async Task RunAsync(string? logFilePath = null, CancellationToken ct = default)
    {
        // Set up optional logging
        if (logFilePath is not null)
        {
            _log = new StreamWriter(logFilePath, append: false) { AutoFlush = true };
            _log.WriteLine($"[dotori-lsp] Server started at {DateTime.UtcNow:O}");
        }

        var transport = new LspTransport(Console.OpenStandardInput(), Console.OpenStandardOutput(), _log);
        var store     = new DocumentStore();
        bool shutdown = false;

        TextDocumentHandler? textDocHandler = null;

        while (!ct.IsCancellationRequested)
        {
            string? json;
            try
            {
                json = await transport.ReadMessageAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (json is null) break;

            var request = LspSerializer.DeserializeRequest(json);
            if (request is null)
            {
                _log?.WriteLine($"[dotori-lsp] Could not parse request: {json}");
                continue;
            }

            _log?.WriteLine($"[dotori-lsp] Handling: {request.Method}");

            // Dispatch
            switch (request.Method)
            {
                case "initialize":
                {
                    var result = InitializeHandler.Handle();
                    textDocHandler = new TextDocumentHandler(store, (uri, diags) =>
                        _ = PublishDiagnosticsAsync(transport, uri, diags, ct));

                    await SendResponseAsync(transport, request.Id, result, ct);
                    break;
                }

                case "initialized":
                    // Notification — no response needed
                    break;

                case "shutdown":
                    shutdown = true;
                    await SendResponseAsync(transport, request.Id, new object(), ct);
                    break;

                case "exit":
                    // After shutdown+exit, the server should terminate with code 0
                    // If exit comes without prior shutdown, use code 1
                    Environment.Exit(shutdown ? 0 : 1);
                    return;

                case "textDocument/didOpen":
                    if (textDocHandler is not null && request.Params.HasValue)
                    {
                        var p = LspSerializer.DeserializeElement<DidOpenTextDocumentParams>(request.Params.Value);
                        if (p is not null) textDocHandler.HandleDidOpen(p);
                    }
                    break;

                case "textDocument/didChange":
                    if (textDocHandler is not null && request.Params.HasValue)
                    {
                        var p = LspSerializer.DeserializeElement<DidChangeTextDocumentParams>(request.Params.Value);
                        if (p is not null) textDocHandler.HandleDidChange(p);
                    }
                    break;

                case "textDocument/didClose":
                    if (textDocHandler is not null && request.Params.HasValue)
                    {
                        var p = LspSerializer.DeserializeElement<DidCloseTextDocumentParams>(request.Params.Value);
                        if (p is not null) textDocHandler.HandleDidClose(p);
                    }
                    break;

                case "textDocument/completion":
                    if (request.Params.HasValue)
                    {
                        var p = LspSerializer.DeserializeElement<CompletionParams>(request.Params.Value);
                        if (p is not null)
                        {
                            var doc = store.Get(p.TextDocument.Uri);
                            var text = doc?.Text ?? "";
                            var list = CompletionProvider.GetCompletions(
                                text, p.Position.Line, p.Position.Character);
                            await SendResponseAsync(transport, request.Id, list, ct);
                        }
                        else
                        {
                            await SendNullResponseAsync(transport, request.Id, ct);
                        }
                    }
                    else
                    {
                        await SendNullResponseAsync(transport, request.Id, ct);
                    }
                    break;

                case "textDocument/hover":
                    if (request.Params.HasValue)
                    {
                        var p = LspSerializer.DeserializeElement<HoverParams>(request.Params.Value);
                        if (p is not null)
                        {
                            var doc = store.Get(p.TextDocument.Uri);
                            var text = doc?.Text ?? "";
                            var hover = HoverProvider.GetHover(text, p.Position.Line, p.Position.Character);
                            await SendResponseAsync(transport, request.Id, (object?)hover, ct);
                        }
                        else
                        {
                            await SendNullResponseAsync(transport, request.Id, ct);
                        }
                    }
                    else
                    {
                        await SendNullResponseAsync(transport, request.Id, ct);
                    }
                    break;

                case "textDocument/definition":
                    if (request.Params.HasValue)
                    {
                        var p = LspSerializer.DeserializeElement<DefinitionParams>(request.Params.Value);
                        if (p is not null)
                        {
                            var doc = store.Get(p.TextDocument.Uri);
                            var text = doc?.Text ?? "";
                            var location = DefinitionProvider.GetDefinition(
                                text, p.Position.Line, p.Position.Character, doc?.LocalPath);
                            await SendResponseAsync(transport, request.Id, (object?)location, ct);
                        }
                        else
                        {
                            await SendNullResponseAsync(transport, request.Id, ct);
                        }
                    }
                    else
                    {
                        await SendNullResponseAsync(transport, request.Id, ct);
                    }
                    break;

                case "textDocument/references":
                    if (request.Params.HasValue)
                    {
                        var p = LspSerializer.DeserializeElement<ReferenceParams>(request.Params.Value);
                        if (p is not null)
                        {
                            var doc = store.Get(p.TextDocument.Uri);
                            var locations = OptionSymbolProvider.GetReferences(
                                doc?.Text ?? "", p.TextDocument.Uri,
                                p.Position.Line, p.Position.Character,
                                p.Context.IncludeDeclaration);
                            await SendResponseAsync(transport, request.Id, locations, ct);
                        }
                        else await SendNullResponseAsync(transport, request.Id, ct);
                    }
                    else await SendNullResponseAsync(transport, request.Id, ct);
                    break;

                case "textDocument/prepareRename":
                    if (request.Params.HasValue)
                    {
                        var p = LspSerializer.DeserializeElement<DefinitionParams>(request.Params.Value);
                        if (p is not null)
                        {
                            var doc = store.Get(p.TextDocument.Uri);
                            var range = OptionSymbolProvider.PrepareRename(
                                doc?.Text ?? "", p.Position.Line, p.Position.Character);
                            await SendResponseAsync(transport, request.Id, range, ct);
                        }
                        else await SendNullResponseAsync(transport, request.Id, ct);
                    }
                    else await SendNullResponseAsync(transport, request.Id, ct);
                    break;

                case "textDocument/rename":
                    if (request.Params.HasValue)
                    {
                        var p = LspSerializer.DeserializeElement<RenameParams>(request.Params.Value);
                        if (p is not null)
                        {
                            var doc = store.Get(p.TextDocument.Uri);
                            var edit = OptionSymbolProvider.Rename(
                                doc?.Text ?? "", p.TextDocument.Uri,
                                p.Position.Line, p.Position.Character, p.NewName);
                            await SendResponseAsync(transport, request.Id, edit, ct);
                        }
                        else await SendNullResponseAsync(transport, request.Id, ct);
                    }
                    else await SendNullResponseAsync(transport, request.Id, ct);
                    break;

                case "textDocument/formatting":
                    if (request.Params.HasValue)
                    {
                        var p = LspSerializer.DeserializeElement<DocumentFormattingParams>(request.Params.Value);
                        if (p is not null)
                        {
                            var doc = store.Get(p.TextDocument.Uri);
                            var edits = FormattingProvider.Format(doc?.Text ?? "", doc?.LocalPath);
                            await SendResponseAsync(transport, request.Id, edits, ct);
                        }
                        else await SendNullResponseAsync(transport, request.Id, ct);
                    }
                    else await SendNullResponseAsync(transport, request.Id, ct);
                    break;

                case "$/cancelRequest":
                    // Ignore cancel (we process synchronously)
                    break;

                default:
                    // Unknown request — send method not found error if it has an id
                    if (request.Id is not null)
                    {
                        await SendErrorAsync(transport, request.Id, -32601,
                            $"Method not found: {request.Method}", ct);
                    }
                    break;
            }
        }

        _log?.WriteLine("[dotori-lsp] Server exiting.");
        _log?.Dispose();
    }

    private static async Task PublishDiagnosticsAsync(
        LspTransport transport,
        string uri,
        List<LspDiagnostic> diagnostics,
        CancellationToken ct)
    {
        var notification = new JsonRpcNotification
        {
            Method = "textDocument/publishDiagnostics",
            Params = new PublishDiagnosticsParams
            {
                Uri         = uri,
                Diagnostics = diagnostics,
            },
        };

        var json = SerializeNotification(notification);
        await transport.WriteMessageAsync(json, ct);
    }

    private static async Task SendResponseAsync(
        LspTransport transport,
        JsonElement? id,
        object? result,
        CancellationToken ct)
    {
        var response = new JsonRpcResponse { Id = id, Result = result };
        var json = SerializeResponse(response, result);
        await transport.WriteMessageAsync(json, ct);
    }

    private static async Task SendNullResponseAsync(
        LspTransport transport,
        JsonElement? id,
        CancellationToken ct)
    {
        var json = BuildNullResponse(id);
        await transport.WriteMessageAsync(json, ct);
    }

    private static async Task SendErrorAsync(
        LspTransport transport,
        JsonElement? id,
        int code, string message,
        CancellationToken ct)
    {
        var response = new JsonRpcResponse
        {
            Id    = id,
            Error = new JsonRpcError { Code = code, Message = message },
        };
        var json = SerializeResponseRaw(response);
        await transport.WriteMessageAsync(json, ct);
    }

    // ─── Serialization helpers ─────────────────────────────────────────────

    private static string SerializeResponse(JsonRpcResponse envelope, object? result)
    {
        // We need to embed result as raw JSON, so we build the JSON manually.
        // Use a switch on result type to get the right TypeInfo.
        var resultJson = result switch
        {
            null                   => "null",
            InitializeResult r     => LspSerializer.Serialize(r),
            CompletionList cl      => LspSerializer.Serialize(cl),
            LspHover h             => LspSerializer.Serialize(h),
            LspLocation loc        => LspSerializer.Serialize(loc),
            LspLocation[] locations => LspSerializer.Serialize(locations),
            LspRange range         => LspSerializer.Serialize(range),
            LspWorkspaceEdit edit  => LspSerializer.Serialize(edit),
            List<LspTextEdit> edits => LspSerializer.Serialize(edits),
            // For plain object{} (shutdown response) just use null-like
            _                      => "{}",
        };

        return BuildResponseJson(envelope.Id, resultJson);
    }

    private static string SerializeResponseRaw(JsonRpcResponse response)
    {
        return LspSerializer.Serialize(response);
    }

    private static string SerializeNotification(JsonRpcNotification notification)
    {
        // Params is PublishDiagnosticsParams
        if (notification.Params is PublishDiagnosticsParams p)
        {
            var paramsJson = LspSerializer.Serialize(p);
            return $"{{\"jsonrpc\":\"2.0\",\"method\":{JsonEncodedText.Encode(notification.Method)},\"params\":{paramsJson}}}";
        }
        return LspSerializer.SerializeNotification(notification);
    }

    private static string BuildResponseJson(JsonElement? id, string resultJson)
    {
        var idJson = id switch
        {
            null                                          => "null",
            { ValueKind: JsonValueKind.Number } e         => e.GetRawText(),
            { ValueKind: JsonValueKind.String } e         => $"\"{e.GetString()}\"",
            _                                             => "null",
        };
        return $"{{\"jsonrpc\":\"2.0\",\"id\":{idJson},\"result\":{resultJson}}}";
    }

    private static string BuildNullResponse(JsonElement? id)
    {
        return BuildResponseJson(id, "null");
    }
}
