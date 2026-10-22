using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dotori.VisualStudio;

[ContentType(DotoriContentType.Name)]
[Export(typeof(ILanguageClient))]
internal sealed class DotoriLanguageClient : ILanguageClient
{
    private Process? process;

    public string Name => "Dotori Language Server";

    public IEnumerable<string>? ConfigurationSections => null;

    public object? InitializationOptions => null;

    public IEnumerable<string>? FilesToWatch => new[] { "**/.dotori" };

    public bool ShowNotificationOnInitializeFailed => true;

#pragma warning disable CS0067
    public event AsyncEventHandler<EventArgs>? StartAsync;

    public event AsyncEventHandler<EventArgs>? StopAsync;
#pragma warning restore CS0067

    public Task<Connection?> ActivateAsync(CancellationToken token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotori",
            Arguments = "lsp --watch-ide",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start 'dotori lsp'. Ensure dotori is available on PATH.");
        }

        _ = DrainStandardErrorAsync(process.StandardError, token);
        return Task.FromResult<Connection?>(
            new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream));
    }

    public async Task OnLoadedAsync()
    {
        await StartAsync.InvokeAsync(this, EventArgs.Empty).ConfigureAwait(false);
    }

    public Task OnServerInitializedAsync()
    {
        return Task.CompletedTask;
    }

    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(
        ILanguageClientInitializationInfo initializationState)
    {
        return Task.FromResult<InitializationFailureContext?>(null);
    }

    private static async Task DrainStandardErrorAsync(StreamReader reader, CancellationToken token)
    {
        try
        {
            while (!reader.EndOfStream && !token.IsCancellationRequested)
            {
                await reader.ReadLineAsync().ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
