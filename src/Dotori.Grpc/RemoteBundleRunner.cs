using System.Collections.Concurrent;
using System.Diagnostics;
using Dotori.Core.Executor;

namespace Dotori.Grpc;

public sealed record RemoteBundleResult(bool Success, int ExitCode, string Stdout, string Stderr, BundleFile[] Outputs);

/// <summary>Materializes verified content and executes the supplied toolchain in an isolated action directory.</summary>
public sealed class RemoteBundleRunner(RemoteContentStore store)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ToolchainLocks = new(StringComparer.Ordinal);

    public async Task<RemoteBundleResult> RunAsync(
        ExecutionBundle bundle, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        Validate(bundle);
        var toolchainDirectory = await MaterializeToolchainAsync(bundle.Toolchain, ct);
        var actionsDirectory = Path.Combine(store.DirectoryPath, "actions");
        Directory.CreateDirectory(actionsDirectory);
        var directory = Path.Combine(actionsDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await MaterializeFilesAsync(directory, bundle.Inputs, ct);
            var output = RemoteContentStore.SafePath(directory, bundle.OutputPath);
            var outputDirectory = Path.GetDirectoryName(output)!;
            Directory.CreateDirectory(outputDirectory);
            var workingDirectory = RemoteContentStore.SafePath(directory, bundle.WorkingDirectory);
            Directory.CreateDirectory(workingDirectory);
            var executable = RemoteContentStore.SafePath(toolchainDirectory, bundle.Executable);
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var key in new[] { "INCLUDE", "LIB", "LIBPATH", "CPATH", "CPLUS_INCLUDE_PATH", "LIBRARY_PATH", "SDKROOT" })
                start.Environment.Remove(key);
            foreach (var variable in bundle.Toolchain.Environment)
                start.Environment[variable.Key] = Materialize(variable.Value, toolchainDirectory, directory);
            start.Environment["PATH"] = bundle.Toolchain.Environment.TryGetValue("PATH", out var path)
                ? Materialize(path, toolchainDirectory, directory) : Path.GetDirectoryName(executable)!;
            start.Environment["PATH"] = Path.GetDirectoryName(executable) + Path.PathSeparator + start.Environment["PATH"];
            var args = arguments.Select(argument => Materialize(argument, toolchainDirectory, directory)).ToArray();
            using var invocation = ResponseFileArguments.CreateExpanded(args, directory: directory);
            foreach (var argument in invocation.Arguments) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(timeout);
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
                ct.ThrowIfCancellationRequested();
                return new RemoteBundleResult(false, -1, await stdout, "Bundled action exceeded its execution timeout.", []);
            }
            var standardOutput = await stdout;
            var standardError = await stderr;
            if (process.ExitCode != 0)
                return new RemoteBundleResult(false, process.ExitCode, standardOutput, standardError, []);
            if (!File.Exists(output))
                return new RemoteBundleResult(false, -1, standardOutput, "Bundled action did not create its primary output.", []);

            var inputs = bundle.Inputs.ToDictionary(file => file.Path, file => file.Hash,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var outputs = new List<BundleFile>();
            foreach (var file in EnumerateOutputFiles(outputDirectory))
            {
                var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                var hash = await store.ImportFileAsync(file, ct);
                if (relative != bundle.OutputPath && inputs.TryGetValue(relative, out var previous) && previous == hash) continue;
                outputs.Add(new BundleFile
                {
                    Path = relative, Hash = hash, Size = new FileInfo(file).Length,
                    Executable = !OperatingSystem.IsWindows() && (File.GetUnixFileMode(file) &
                        (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0,
                });
            }
            return new RemoteBundleResult(true, 0, standardOutput, standardError, outputs.ToArray());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static void Validate(ExecutionBundle bundle)
    {
        if (bundle.Toolchain is null || bundle.Toolchain.HostOs != RemoteBundleClient.HostOs ||
            bundle.Toolchain.HostArch != RemoteBundleClient.HostArch)
            throw new InvalidDataException("Uploaded toolchain requires the same host OS and CPU architecture.");
        if (bundle.Toolchain.Id != RemoteContentStore.ToolchainId(bundle.Toolchain) ||
            bundle.Id != RemoteContentStore.ActionId(bundle))
            throw new InvalidDataException("Bundle manifest digest mismatch.");
        ValidateFiles(bundle.Toolchain.Files);
        ValidateFiles(bundle.Inputs);
        _ = RemoteContentStore.SafePath(Path.GetTempPath(), bundle.WorkingDirectory);
        _ = RemoteContentStore.SafePath(Path.GetTempPath(), bundle.OutputPath);
        _ = RemoteContentStore.SafePath(Path.GetTempPath(), bundle.Executable);
        if (!bundle.Toolchain.Files.Any(file => file.Path == bundle.Executable))
            throw new InvalidDataException("Executable is not in the toolchain manifest.");
    }

    private static void ValidateFiles(IEnumerable<BundleFile> files)
    {
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var file in files)
        {
            _ = RemoteContentStore.SafePath(Path.GetTempPath(), file.Path);
            if (!RemoteContentStore.IsHash(file.Hash) || file.Size < 0 || !paths.Add(file.Path))
                throw new InvalidDataException("Invalid or duplicate bundle file.");
        }
    }

    private async Task<string> MaterializeToolchainAsync(ToolchainBundle bundle, CancellationToken ct)
    {
        var directory = Path.Combine(store.DirectoryPath, "toolchains", bundle.Id);
        var gate = ToolchainLocks.GetOrAdd(directory, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(ct);
        try
        {
            if (Directory.Exists(directory)) return directory;
            var staging = directory + "." + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            try
            {
                await MaterializeFilesAsync(staging, bundle.Files, ct);
                try { Directory.Move(staging, directory); }
                catch (IOException) when (Directory.Exists(directory)) { }
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            return directory;
        }
        finally { gate.Release(); }
    }

    private async Task MaterializeFilesAsync(string directory, IEnumerable<BundleFile> files, CancellationToken ct)
    {
        foreach (var file in files)
        {
            var source = store.BlobPath(file.Hash);
            if (!File.Exists(source) || new FileInfo(source).Length != file.Size)
                throw new InvalidDataException($"Bundle content is missing or has the wrong length: {file.Path}");
            var target = RemoteContentStore.SafePath(directory, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using (var input = File.OpenRead(source))
            await using (var output = File.Create(target))
                await input.CopyToAsync(output, ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, file.Executable ? (UnixFileMode)493 : (UnixFileMode)420);
        }
    }

    private static string Materialize(string value, string tools, string workspace) => value
        .Replace(RemoteBundleClient.ToolchainPlaceholder, tools, StringComparison.Ordinal)
        .Replace(RemoteBundleClient.WorkspacePlaceholder, workspace, StringComparison.Ordinal);

    private static IEnumerable<string> EnumerateOutputFiles(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Bundled actions cannot return symbolic links.");
            if (Directory.Exists(entry))
            { foreach (var file in EnumerateOutputFiles(entry)) yield return file; }
            else yield return entry;
        }
    }
}
