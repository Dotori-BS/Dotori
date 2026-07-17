using System.Text.Json;
using Dotori.Core;
using Dotori.Core.Graph;
using Dotori.Core.Location;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;
using Dotori.PackageManager;

namespace Dotori.Cli;

/// <summary>
/// Shared helper to resolve projects, select targets, detect toolchains,
/// and flatten project models for CLI commands.
/// </summary>
internal static class BuildContext
{
    /// <summary>
    /// Resolve which .dotori files to operate on, using the standard search order.
    /// </summary>
    internal static IReadOnlyList<string> ResolveProjectPaths(
        string? projectArg,
        bool    buildAll)
    {
        if (projectArg is not null)
            return [ProjectLocator.ResolveExplicitPath(projectArg)];

        var found = ProjectLocator.FindDotoriFiles(Directory.GetCurrentDirectory());
        if (found.Count == 0)
        {
            Console.Error.WriteLine("Error: No .dotori file found.");
            return [];
        }

        if (found.Count == 1 || buildAll)
            return found;

        // Multiple projects: load metadata and prompt
        var located = found.Select(ProjectLocator.LoadProject).ToList();
        try
        {
            return ProjectLocator.PromptSelection(located, buildAll);
        }
        catch (ProjectLocatorException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Build the DAG for the given root project paths, checking for cycles.
    /// Returns null on error.
    /// </summary>
    internal static IReadOnlyDictionary<string, ProjectNode>? BuildDag(
        IReadOnlyList<string> rootPaths,
        IReadOnlyDictionary<string, string>? gitPackageMap = null)
    {
        try
        {
            return ProjectDagBuilder.Build(rootPaths, gitPackageMap);
        }
        catch (CircularDependencyException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolve git dependencies from lock files, fetch them if needed,
    /// and return a mapping of package name → local .dotori path.
    /// Packages without a .dotori file (header-only or non-dotori) are skipped.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<string, string>> ResolveAndFetchGitPackagesAsync(
        IReadOnlyList<string> rootPaths,
        bool locked = false,
        bool offline = false,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootPath in rootPaths)
        {
            var projectDir = Path.GetDirectoryName(rootPath)!;

            DotoriFile file;
            try { file = DotoriParser.ParseFile(rootPath); }
            catch { continue; }
            if (file.Project is null) continue;

            // Resolve dependencies (updates lock file if git deps present)
            var existingLock = LockManager.Load(projectDir);
            LockFile lockFile;
            if (HasGitOrVersionDependencies(file.Project))
            {
                try
                {
                    lockFile = await DependencyResolver.ResolveAsync(
                        projectDir, existingLock, file.Project,
                        new DependencyResolver.Options(locked, offline), ct);
                    if (locked && !LockManager.ContentEquals(existingLock, lockFile))
                        throw new PackageManagerException(
                            "Dependency manifest does not match dotori-lock.json (--locked).");
                    if (!locked) LockManager.Save(lockFile, projectDir);
                }
                catch (Exception ex)
                {
                    if (locked || offline) throw;
                    Console.Error.WriteLine($"Warning: dependency resolution failed for '{rootPath}': {ex.Message}");
                    lockFile = existingLock;
                }
            }
            else
            {
                lockFile = existingLock;
            }

            // Fetch each git package and locate its .dotori file
            foreach (var (lockKey, entry) in lockFile.Packages)
            {
                if (!entry.Resolved.StartsWith("git+")) continue;

                // key = "@scope/name@version" or "name@version"
                var atIdx = lockKey.LastIndexOf('@');
                var pkgName = atIdx > 0 ? lockKey[..atIdx] : lockKey;

                if (result.ContainsKey(pkgName)) continue;

                // Parse "git+<url>#<tagOrCommit>"
                var src = entry.Resolved["git+".Length..];
                var hashIdx = src.LastIndexOf('#');
                if (hashIdx < 0) continue;

                var gitUrl      = src[..hashIdx];
                var tagOrCommit = src[(hashIdx + 1)..];

                string localDir;
                try
                {
                    var depsRoot = Path.Combine(projectDir, DotoriConstants.DepsDir);
                    if (offline)
                    {
                        localDir = GitFetcher.GetLocalDepDir(depsRoot, pkgName)
                            ?? throw new PackageManagerException(
                                $"Git package '{pkgName}' is not available in offline mode.");
                    }
                    else
                    {
                        Console.WriteLine($"  Fetching {pkgName} ({tagOrCommit})...");
                        localDir = await GitFetcher.FetchAsync(
                            depsRoot, pkgName, gitUrl, tagOrCommit, ct);
                    }
                }
                catch (Exception ex)
                {
                    if (locked || offline) throw;
                    Console.Error.WriteLine($"Warning: Failed to fetch '{pkgName}': {ex.Message}");
                    continue;
                }

                // Find top-level .dotori file; skip if absent (header-only/non-dotori package)
                var dotoriFile = Directory
                    .GetFiles(localDir, ".dotori", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (dotoriFile is null) continue;

                result[pkgName] = Path.GetFullPath(dotoriFile);
            }
        }

        return result;
    }

    private static bool HasGitOrVersionDependencies(ProjectDecl project) =>
        project.Items
            .OfType<DependenciesBlock>()
            .SelectMany(b => b.Items)
            .Any(i => (i.Value is ComplexDependency d && d.Git is not null) ||
                       i.Value is VersionDependency);

    /// <summary>
    /// Determine the build target ID from CLI options and host environment.
    /// </summary>
    internal static string ResolveTargetId(string? targetArg)
    {
        if (targetArg is not null) return targetArg;

        // Auto-detect from host OS + arch
        var os   = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                       System.Runtime.InteropServices.OSPlatform.Windows) ? "windows"
                 : System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                       System.Runtime.InteropServices.OSPlatform.OSX)     ? "macos"
                 : "linux";

        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64   => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86   => "x86",
            _                                                  => "x64",
        };

        return $"{os}-{arch}";
    }

    /// <summary>
    /// Detect toolchain for the given target. Prints error and returns null on failure.
    /// Priority: <paramref name="cliCompiler"/> &gt; DSL <c>toolchain</c> prop in root project &gt; CXX/CC env &gt; auto-detect.
    /// </summary>
    internal static ToolchainInfo? DetectToolchain(
        string targetId, string? cliCompiler, string? rootDotoriPath = null, string? libc = null)
    {
        var dslToolchain = ReadDslToolchain(rootDotoriPath);
        var compiler = cliCompiler ?? dslToolchain;
        ToolchainInfo? tc;
        try
        {
            tc = compiler is not null && ToolchainProfile.IsProfilePath(compiler)
                ? ToolchainProfile.Load(compiler,
                    dslToolchain is not null && rootDotoriPath is not null
                        ? Path.GetDirectoryName(Path.GetFullPath(rootDotoriPath))
                        : Environment.CurrentDirectory)
                : ToolchainDetector.Detect(targetId, compiler, libc);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            Console.Error.WriteLine($"Error: Invalid toolchain profile: {ex.Message}");
            return null;
        }
        if (tc is null)
            Console.Error.WriteLine(
                $"Error: No toolchain found for target '{targetId}'. " +
                $"Ensure the compiler is installed and in PATH.");
        return tc;
    }

    /// <summary>
    /// Read the <c>toolchain</c> property from the root .dotori file, or null if absent / unreadable.
    /// </summary>
    internal static string? ReadDslToolchain(string? dotoriPath)
    {
        if (dotoriPath is null || !File.Exists(dotoriPath)) return null;
        try
        {
            var file = DotoriParser.ParseFile(dotoriPath);
            return file.Project?.Items.OfType<ToolchainProp>().FirstOrDefault()?.Value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Scans the given .dotori files and collects all top-level option declarations.
    /// Returns a dictionary of option name → default value.
    /// If the same option name appears in multiple files, the first declaration wins.
    /// </summary>
    internal static Dictionary<string, bool> ScanOptions(IEnumerable<string> dotoriPaths)
    {
        var options = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in dotoriPaths)
        {
            DotoriFile file;
            try { file = DotoriParser.ParseFile(path); }
            catch { continue; }
            if (file.Project is null) continue;

            foreach (var item in file.Project.Items.OfType<OptionBlock>())
            {
                if (!options.ContainsKey(item.Name))
                    options[item.Name] = item.Default;
            }
        }
        return options;
    }

    /// <summary>
    /// Resolve the final set of enabled options from declared defaults and CLI overrides.
    /// <paramref name="declaredOptions"/> maps option name → default value.
    /// <paramref name="cliEnabled"/> is the set of options explicitly enabled via --name.
    /// <paramref name="cliDisabled"/> is the set of options explicitly disabled via --no-name.
    /// Returns the set of option names that are active, or null if no options are declared.
    /// </summary>
    internal static IReadOnlySet<string>? ResolveEnabledOptions(
        Dictionary<string, bool> declaredOptions,
        IReadOnlySet<string> cliEnabled,
        IReadOnlySet<string> cliDisabled)
    {
        if (declaredOptions.Count == 0) return null;

        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, defaultVal) in declaredOptions)
        {
            bool active = defaultVal;
            if (cliEnabled.Contains(name))  active = true;
            if (cliDisabled.Contains(name)) active = false;
            if (active) enabled.Add(name);
        }
        return enabled;
    }

    /// <summary>
    /// Build <see cref="TargetContext"/> from CLI arguments.
    /// </summary>
    internal static TargetContext MakeTargetContext(
        string targetId, string config,
        string? runtimeLink = null, string? libc = null, string? stdlib = null,
        IReadOnlySet<string>? enabledOptions = null,
        Dictionary<string, bool>? declaredOptions = null)
    {
        var parts    = targetId.Split('-');
        var platform = parts.Length > 0 ? parts[0] : "linux";
        var compiler = parts.Any(p => p is "msvc") ? "msvc" : "clang";

        // Inject build context into the process environment so .dotori files
        // can reference them via ${DOTORI_CONFIG}, ${DOTORI_PLATFORM}, etc.
        Environment.SetEnvironmentVariable(DotoriConstants.EnvTarget,   targetId);
        Environment.SetEnvironmentVariable(DotoriConstants.EnvConfig,   config);
        Environment.SetEnvironmentVariable(DotoriConstants.EnvPlatform, platform);
        Environment.SetEnvironmentVariable(DotoriConstants.EnvArch,     ExtractArch(parts));

        // Inject option environment variables: DOTORI_OPTION_<NAME>=1/0
        if (declaredOptions != null)
        {
            foreach (var (name, _) in declaredOptions)
            {
                var envName = DotoriConstants.EnvOptionPrefix + name.Replace('-', '_').ToUpperInvariant();
                var isActive = enabledOptions?.Contains(name) ?? false;
                Environment.SetEnvironmentVariable(envName, isActive ? "1" : "0");
            }
        }

        return new TargetContext
        {
            Platform = platform,
            Config   = config,
            Compiler = compiler,
            Runtime  = runtimeLink ?? "static",
            Libc     = libc,
            Stdlib   = stdlib,
            WasmBackend = targetId.Contains("emscripten") ? "emscripten"
                        : targetId.Contains("bare")       ? "bare"
                        : null,
            EnabledOptions = enabledOptions,
        };
    }

    /// <summary>
    /// Extract the CPU architecture identifier from target ID parts.
    /// Known archs: x64, x86, arm64, arm64_32, arm, wasm32.
    /// </summary>
    private static string ExtractArch(string[] parts)
    {
        foreach (var part in parts)
        {
            if (part is "x64" or "x86" or "arm64" or "arm64_32" or "arm" or "wasm32")
                return part;
        }
        // Fallback: last segment (e.g. unknown future arch)
        return parts.Length > 1 ? parts[^1] : "x64";
    }

    /// <summary>
    /// Flatten a project for the given target context.
    /// Optionally injects the public headers from all transitive dependency nodes.
    /// </summary>
    internal static FlatProjectModel? FlattenProject(
        string dotoriPath, TargetContext ctx,
        ProjectNode? node = null)
    {
        try
        {
            var file = DotoriParser.ParseFile(dotoriPath);
            if (file.Project is null)
            {
                Console.Error.WriteLine($"Error: No project declaration in '{dotoriPath}'.");
                return null;
            }

            // Load .dotori.local if it exists (optional per-developer local override)
            ProjectDecl? localDecl = null;
            var localPath = Path.Combine(
                Path.GetDirectoryName(dotoriPath)!,
                DotoriConstants.LocalFileName);
            if (File.Exists(localPath))
            {
                try
                {
                    var localFile = DotoriParser.ParseFile(localPath);
                    localDecl = localFile.Project;
                }
                catch (ParseException ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to parse '{localPath}': {ex.Message}");
                }
            }

            var model = ProjectFlattener.Flatten(file.Project, dotoriPath, ctx, localDecl);

            // Inject public headers from all transitive dependencies
            if (node is not null)
                InjectDepHeaders(model, node, ctx, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            return model;
        }
        catch (ParseException ex)
        {
            Console.Error.WriteLine($"Parse error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Run a list of build scripts (pre-build or post-build) in order.
    /// Each command is executed via the shell. Returns non-zero on first failure.
    /// </summary>
    internal static Task<int> RunScriptsAsync(
        IReadOnlyList<string> commands,
        string                projectDir,
        string                targetId,
        string                config,
        string                outputDir,
        CancellationToken     ct = default)
        => Dotori.Core.Build.ScriptRunner.RunAsync(commands, projectDir, targetId, config, outputDir, ct);

    /// <summary>
    /// Recursively collect public headers from dependency nodes and add them to model.
    /// </summary>
    private static void InjectDepHeaders(
        FlatProjectModel model,
        ProjectNode node,
        TargetContext ctx,
        HashSet<string> visited)
    {
        foreach (var dep in node.Dependencies)
        {
            if (!visited.Add(dep.DotoriPath)) continue;

            try
            {
                var file = DotoriParser.ParseFile(dep.DotoriPath);
                if (file.Project is null) continue;
                var depModel = ProjectFlattener.Flatten(file.Project, dep.DotoriPath, ctx);

                foreach (var h in depModel.Headers.Where(h => h.IsPublic))
                {
                    var absPath = Path.IsPathRooted(h.Path)
                        ? h.Path
                        : Path.GetFullPath(Path.Combine(depModel.ProjectDir, h.Path));
                    // Add only if not already present
                    if (!model.Headers.Any(existing =>
                            string.Equals(existing.Path, absPath, StringComparison.OrdinalIgnoreCase)))
                        model.Headers.Add(new Dotori.Core.Parsing.HeaderItem(isPublic: false, path: absPath));
                }
            }
            catch { /* skip unreadable deps */ }

            InjectDepHeaders(model, dep, ctx, visited);
        }
    }
}
