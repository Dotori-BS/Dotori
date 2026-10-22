using System.CommandLine;
using System.Diagnostics;
using System.Threading.Channels;
using Dotori.Core.Build;

namespace Dotori.Cli.Commands;

internal static class WatchCommandFactory
{
    public static Command Create()
    {
        var command = new Command("watch", "Rebuild or refresh IDE files when project files change");
        var projectOption = new Option<string?>("--project") { Description = "Path to .dotori file or directory" };
        var allOption = new Option<bool>("--all") { Description = "Watch all projects below the directory" };
        var debounceOption = new Option<int>("--debounce")
        {
            Description = "Milliseconds to wait for related file events",
            DefaultValueFactory = _ => 250,
        };
        var noInitialOption = new Option<bool>("--no-initial-build")
            { Description = "Wait for a file change before the first action" };
        var ideOption = new Option<bool>("--ide")
            { Description = "Regenerate IDE integration files instead of building" };
        var clangdOption = new Option<bool>("--clangd")
            { Description = "Regenerate clangd files when Dotori or C/C++ source files change" };

        command.Add(projectOption);
        command.Add(allOption);
        command.Add(debounceOption);
        command.Add(noInitialOption);
        command.Add(ideOption);
        command.Add(clangdOption);
        command.TreatUnmatchedTokensAsErrors = false;

        command.SetAction(async (parseResult, ct) =>
        {
            var projectArg = parseResult.GetValue(projectOption);
            var watchAll = parseResult.GetValue(allOption);
            var debounceMs = parseResult.GetValue(debounceOption);
            var watchIde = parseResult.GetValue(ideOption);
            var watchClangd = parseResult.GetValue(clangdOption);
            if (watchIde && watchClangd)
            {
                Console.Error.WriteLine("Error: --ide and --clangd cannot be used together.");
                return 1;
            }
            if (debounceMs < 0)
            {
                Console.Error.WriteLine("Error: --debounce must be zero or greater.");
                return 1;
            }

            var paths = BuildContext.ResolveProjectPaths(projectArg, watchAll);
            if (paths.Count == 0) return 1;
            var actionArgs = CreateActionArguments(
                projectArg, watchAll, watchIde, watchClangd, parseResult.UnmatchedTokens, paths);

            if (!parseResult.GetValue(noInitialOption))
            {
                var initialCode = await RunActionAsync(actionArgs, ct);
                if (initialCode != 0)
                    Console.Error.WriteLine($"  Initial {ActionName(watchIde, watchClangd)} failed (exit {initialCode}); continuing to watch.");
            }

            var changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            });
            using var registration = ct.Register(() => changes.Writer.TryComplete());
            using var watchers = new WatcherCollection(paths, changes.Writer, watchClangd);

            Console.WriteLine($"  Watching {watchers.RootCount} project root(s). Press Ctrl+C to stop.");
            try
            {
                await foreach (var signal in changes.Reader.ReadAllAsync(ct))
                {
                    if (debounceMs > 0)
                        await Task.Delay(debounceMs, ct);
                    while (changes.Reader.TryRead(out var pendingSignal)) { }

                    Console.WriteLine($"\n  Change detected at {DateTime.Now:T}; {ActionVerb(watchIde, watchClangd)}...");
                    var exitCode = await RunActionAsync(actionArgs, ct);
                    Console.WriteLine(exitCode == 0
                        ? $"  {ActionName(watchIde, watchClangd)} completed."
                        : $"  {ActionName(watchIde, watchClangd)} failed (exit {exitCode}); waiting for changes.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Normal Ctrl+C shutdown.
            }
            return 0;
        });
        return command;
    }

    private static IReadOnlyList<string> CreateActionArguments(
        string? projectArg,
        bool all,
        bool ide,
        bool clangd,
        IReadOnlyList<string> forwarded,
        IReadOnlyList<string> projectPaths)
    {
        List<string> args;
        var includeAll = all;
        if (clangd)
        {
            includeAll |= projectArg is null && projectPaths.Count > 1;
            var project = projectArg ?? (includeAll ? Directory.GetCurrentDirectory() : projectPaths[0]);
            var outputDirectory = Directory.Exists(project)
                ? Path.GetFullPath(project)
                : Path.GetDirectoryName(Path.GetFullPath(project))!;
            args =
            [
                "export", "ide", "--project", project,
                "--output", Path.Combine(outputDirectory, "compile_commands.json"),
                "--clangd-output", Path.Combine(outputDirectory, ".clangd"),
            ];
        }
        else if (ide)
        {
            args = ["setup", "ide", "--ide", "all"];
        }
        else
        {
            args = ["build"];
        }

        if (projectArg is not null)
        {
            if (!clangd)
            {
                args.Add("--project");
                args.Add(projectArg);
            }
        }
        if (includeAll)
        {
            args.Add("--all");
        }
        args.AddRange(forwarded);
        return args;
    }

    private static string ActionName(bool ide, bool clangd) =>
        clangd ? "clangd refresh" : ide ? "IDE refresh" : "Build";

    private static string ActionVerb(bool ide, bool clangd) =>
        clangd ? "refreshing clangd files" : ide ? "refreshing IDE files" : "rebuilding";

    private static async Task<int> RunActionAsync(
        IReadOnlyList<string> arguments,
        CancellationToken ct)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to locate the current Dotori executable.");
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Environment.GetCommandLineArgs().FirstOrDefault();
            if (string.IsNullOrWhiteSpace(assemblyPath) ||
                !assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                assemblyPath = Path.Combine(AppContext.BaseDirectory, "dotori.dll");
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new InvalidOperationException("Unable to locate the Dotori CLI assembly.");
            startInfo.ArgumentList.Add(assemblyPath);
        }
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the Dotori build process.");
        try
        {
            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    private sealed class WatcherCollection : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = [];
        private readonly bool _clangd;
        public int RootCount => _watchers.Count;

        public WatcherCollection(
            IReadOnlyList<string> projectPaths,
            ChannelWriter<bool> changes,
            bool clangd)
        {
            _clangd = clangd;
            foreach (var root in projectPaths.Select(path => Path.GetDirectoryName(path)!)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = false,
                };
                FileSystemEventHandler onChange = (_, e) => Signal(e.FullPath, e.ChangeType, changes);
                RenamedEventHandler onRename = (_, e) =>
                {
                    Signal(e.FullPath, e.ChangeType, changes);
                    Signal(e.OldFullPath, e.ChangeType, changes);
                };
                watcher.Changed += onChange;
                watcher.Created += onChange;
                watcher.Deleted += onChange;
                watcher.Renamed += onRename;
                watcher.Error += (_, e) =>
                    Console.Error.WriteLine($"  Watcher error for {root}: {e.GetException().Message}");
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }

        private void Signal(string path, WatcherChangeTypes changeType, ChannelWriter<bool> changes)
        {
            var shouldTrigger = _clangd
                ? BuildWatchFilter.ShouldTriggerClangd(path, changeType)
                : BuildWatchFilter.ShouldTrigger(path);
            if (shouldTrigger) changes.TryWrite(true);
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers) watcher.Dispose();
        }
    }
}
