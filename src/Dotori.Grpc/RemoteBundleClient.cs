using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using Dotori.Core.Build;
using Dotori.Core.Executor;
using Grpc.Core;

namespace Dotori.Grpc;

public sealed class RemoteBundleConfiguration
{
    public string[] ToolchainRoots { get; set; } = [];
    public string[] InputRoots { get; set; } = [];
    public string[] InputExcludes { get; set; } = [];
    public Dictionary<string, string> Environment { get; set; } = [];
    public string[] InheritEnvironment { get; set; } = [];
    public string? WorkingDirectory { get; set; }
}

[JsonSerializable(typeof(RemoteBundleConfiguration))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class RemoteBundleJsonContext : JsonSerializerContext;

/// <summary>Snapshots explicitly declared roots and uploads only missing file contents.</summary>
public sealed class RemoteBundleClient
{
    public const string ToolchainPlaceholder = "{DOTORI_TOOLCHAIN}";
    public const string WorkspacePlaceholder = "{DOTORI_WORKSPACE}";
    private readonly RemoteBundleConfiguration _configuration;
    private readonly string[] _toolRoots;
    private readonly string[] _inputRoots;
    private readonly string _workingDirectory;
    private readonly Dictionary<string, string> _localFiles = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1);
    private readonly RemoteContentStore _downloads;
    private ToolchainBundle? _toolchain;

    public RemoteBundleClient(string configurationPath)
    {
        configurationPath = Path.GetFullPath(configurationPath);
        _configuration = JsonSerializer.Deserialize(File.ReadAllText(configurationPath),
            RemoteBundleJsonContext.Default.RemoteBundleConfiguration)
            ?? throw new InvalidDataException("Empty remote toolchain configuration.");
        var directory = Path.GetDirectoryName(configurationPath)!;
        _toolRoots = ResolveRoots(_configuration.ToolchainRoots, directory);
        _inputRoots = ResolveRoots(_configuration.InputRoots, directory);
        _workingDirectory = Path.GetFullPath(_configuration.WorkingDirectory ?? ".", directory);
        if (_toolRoots.Length == 0 || _inputRoots.Length == 0)
            throw new InvalidDataException("remote toolchain configuration requires toolchainRoots and inputRoots.");
        _downloads = new RemoteContentStore(Path.Combine(directory, ".dotori-cache", "remote-content"));
    }

    public static string HostOs => OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unsupported";
    public static string HostArch => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    public async Task<(ExecutionBundle Bundle, string[] Arguments)> PrepareAsync(
        string executable, IEnumerable<string> arguments, string outputFile,
        IEnumerable<string> explicitInputs, CancellationToken ct)
    {
        // A single immutable toolchain snapshot and shared upload map per build invocation.
        await _gate.WaitAsync(ct);
        try
        {
            if (_toolchain is null)
            {
                var snapshot = new ToolchainBundle { HostOs = HostOs, HostArch = HostArch };
                foreach (var file in _toolRoots.SelectMany(EnumerateRoot).Distinct(
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                    snapshot.Files.Add(await CaptureAsync(file, MapToolchainPath(file), ct));
                foreach (var variable in _configuration.InheritEnvironment)
                    if (System.Environment.GetEnvironmentVariable(variable) is { } value)
                        snapshot.Environment[variable] = Map(value);
                foreach (var variable in _configuration.Environment)
                    snapshot.Environment[variable.Key] = Map(variable.Value);
                var paths = snapshot.Files.Where(file => file.Executable || file.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    .Select(file => ToolchainPlaceholder + "/" + file.Path[..file.Path.LastIndexOf('/')])
                    .Distinct().OrderBy(path => path, StringComparer.Ordinal);
                // No inherited toolchain PATH: child tools must come from this bundle.
                snapshot.Environment["PATH"] = string.Join(Path.PathSeparator, paths);
                snapshot.Id = RemoteContentStore.ToolchainId(snapshot);
                _toolchain = snapshot;
            }

            executable = ResolveExecutable(executable);
            var mappedExecutable = MapToolchainPath(executable);
            if (!_toolchain.Files.Any(file => file.Path == mappedExecutable))
                throw new InvalidDataException($"Executable is absent from toolchainRoots: {executable}");
            var bundle = new ExecutionBundle
            {
                Toolchain = _toolchain,
                Executable = mappedExecutable,
                OutputPath = MapInputPath(Path.GetFullPath(outputFile)),
                WorkingDirectory = MapInputPath(_workingDirectory),
            };
            var files = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var root in _inputRoots)
                foreach (var file in EnumerateRoot(root))
                    if (!IsToolchainPath(file) && !_configuration.InputExcludes.Any(pattern =>
                        System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern.Replace('\\', '/'),
                            Path.GetRelativePath(root, file).Replace('\\', '/'), OperatingSystem.IsWindows())))
                        files.Add(file);
            foreach (var file in explicitInputs)
                if (File.Exists(file) && !IsToolchainPath(file)) files.Add(Path.GetFullPath(file));
            var expanded = ProcessArguments.Expand(arguments);
            foreach (var argument in expanded)
            {
                var candidate = argument;
                foreach (var prefix in new[] { "-include-pch", "/Fp", "/FI", "-include", "@" })
                    if (candidate.StartsWith(prefix, StringComparison.Ordinal)) candidate = candidate[prefix.Length..];
                if (Path.IsPathRooted(candidate) && File.Exists(candidate) && !IsToolchainPath(candidate))
                    files.Add(Path.GetFullPath(candidate));
            }
            files.Remove(Path.GetFullPath(outputFile));
            foreach (var file in files.OrderBy(path => path, StringComparer.Ordinal))
                bundle.Inputs.Add(await CaptureAsync(file, MapInputPath(file), ct));
            bundle.Id = RemoteContentStore.ActionId(bundle);
            return (bundle, expanded.Select(Map).ToArray());
        }
        finally { _gate.Release(); }
    }

    public async Task UploadAsync(ExecutionBundle bundle, BuildCoordinator.BuildCoordinatorClient client,
        Metadata headers, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await RemoteContentTransfer.UploadMissingAsync(bundle.Toolchain.Files.Concat(bundle.Inputs),
                hash => _localFiles[hash],
                async query => await client.FindMissingBlobsAsync(query, headers, cancellationToken: ct),
                () => client.UploadBlob(headers, cancellationToken: ct), ct);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveOutputsAsync(IEnumerable<BundleFile> outputs, string outputFile,
        BuildCoordinator.BuildCoordinatorClient client, Metadata headers, CancellationToken ct)
    {
        var files = outputs.ToArray();
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFile))!;
        var destinations = files.Select(file => (File: file, Path: UnmapInputPath(file.Path))).ToArray();
        foreach (var item in destinations)
            if (!IsUnder(item.Path, outputDirectory))
                throw new InvalidDataException("Remote output is outside the requested output directory.");
        if (!destinations.Any(item => PathsEqual(item.Path, Path.GetFullPath(outputFile))))
            throw new InvalidDataException("Remote action did not return its primary output.");
        await RemoteContentTransfer.FetchMissingAsync(files, _downloads,
            query => client.DownloadBlobs(query, headers, cancellationToken: ct), ct);
        foreach (var item in destinations)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
            var temporary = item.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(_downloads.BlobPath(item.File.Hash), temporary);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporary, item.File.Executable ? (UnixFileMode)493 : (UnixFileMode)420);
                File.Move(temporary, item.Path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private async Task<BundleFile> CaptureAsync(string path, string relative, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
        _localFiles[hash] = path;
        return new BundleFile
        {
            Path = relative, Hash = hash, Size = stream.Length,
            Executable = !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0,
        };
    }

    private string Map(string value)
    {
        var roots = _toolRoots.Select((root, index) => (Root: root, Replacement: ToolchainPlaceholder + "/r" + index))
            .Concat(_inputRoots.Select((root, index) => (Root: root, Replacement: WorkspacePlaceholder + "/r" + index)))
            .OrderByDescending(item => item.Root.Length);
        foreach (var item in roots)
        {
            var source = item.Root;
            var replacement = item.Replacement;
            if (File.Exists(source)) replacement += "/" + Path.GetFileName(source);
            value = ReplaceRoot(value, source, replacement);
            value = ReplaceRoot(value, source.Replace('\\', '/'), replacement);
        }
        return value;
    }

    private static string ReplaceRoot(string value, string root, string replacement)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var start = 0;
        while ((start = value.IndexOf(root, start, comparison)) >= 0)
        {
            var end = start + root.Length;
            if (end == value.Length || value[end] is '/' or '\\' or ';' or ':' or '"')
            { value = value[..start] + replacement + value[end..]; start += replacement.Length; }
            else start = end;
        }
        return value;
    }

    private string MapToolchainPath(string path) => MapRootPath(_toolRoots, path);
    private string MapInputPath(string path) => MapRootPath(_inputRoots, path);
    private bool IsToolchainPath(string path) => _toolRoots.Any(root => IsUnder(Path.GetFullPath(path), root));

    private static string MapRootPath(string[] roots, string path)
    {
        foreach (var index in Enumerable.Range(0, roots.Length).OrderByDescending(index => roots[index].Length))
            if (IsUnder(path, roots[index])) return RootRelative(roots[index], index, path);
        throw new InvalidDataException($"Path is not covered by the declared bundle roots: {path}");
    }

    private string UnmapInputPath(string relative)
    {
        for (var index = 0; index < _inputRoots.Length; index++)
        {
            var prefix = "r" + index + "/";
            if (relative.StartsWith(prefix, StringComparison.Ordinal))
                return RemoteContentStore.SafePath(_inputRoots[index], relative[prefix.Length..]);
        }
        throw new InvalidDataException("Unknown output root.");
    }

    private static string RootRelative(string root, int index, string path) =>
        !File.Exists(root) && PathsEqual(path, root) ? "r" + index : "r" + index + "/" +
        (File.Exists(root) ? Path.GetFileName(root) : Path.GetRelativePath(root, path).Replace('\\', '/'));

    private static bool PathsEqual(string left, string right) => string.Equals(left, right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool IsUnder(string path, string root) => PathsEqual(path, root) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string[] ResolveRoots(string[] roots, string directory) => roots
        .Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root, directory))).ToArray();

    private static IEnumerable<string> EnumerateRoot(string root)
    {
        if (File.Exists(root)) { yield return root; yield break; }
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).OrderBy(path => path, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(entry);
            if (name is ".git" or ".dotori-cache" or ".aws" or ".ssh" or ".gnupg" ||
                name.StartsWith(".env", StringComparison.Ordinal)) continue;
            if (Directory.Exists(entry))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    continue; // Directory links must be declared as explicit roots to avoid implicit traversal.
                foreach (var file in EnumerateRoot(entry)) yield return file;
            }
            else yield return entry; // File links are dereferenced into regular content.
        }
    }

    private static string ResolveExecutable(string executable)
    {
        if (File.Exists(executable)) return Path.GetFullPath(executable);
        foreach (var directory in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(directory, executable);
            if (File.Exists(path)) return Path.GetFullPath(path);
            if (OperatingSystem.IsWindows() && File.Exists(path + ".exe")) return Path.GetFullPath(path + ".exe");
        }
        throw new FileNotFoundException("Cannot resolve bundled executable.", executable);
    }
}
