using Dotori.Core;
using Dotori.Core.Linker;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Core.Build;

/// <summary>
/// The primary entry point for planning a build.
/// Given a flat project model and toolchain, produces all compile + link jobs.
/// Supports PCH, C++ Modules (with ModuleScanner/ModuleSorter), Unity Build,
/// incremental build, and the Apple/LLD linker selection.
///
/// Split into partial files:
///   BuildPlanner.cs          — constructor, glob helpers, compile/PCH/module jobs
///   BuildPlanner.Link.cs     — link job, linker selection, output name
///   BuildPlanner.Artifacts.cs — artifact copy (output { } block)
///   BuildPlanner.Windows.cs  — Windows RC / manifest handling
/// </summary>
public sealed partial class BuildPlanner
{
    private readonly FlatProjectModel _model;
    private readonly ToolchainInfo    _toolchain;
    private readonly string           _config;
    private readonly string           _targetId;
    private readonly string           _cacheDir;

    public BuildPlanner(
        FlatProjectModel model,
        ToolchainInfo    toolchain,
        string           config,
        string           targetId)
    {
        _model     = model;
        _toolchain = toolchain;
        _config    = config;
        _targetId  = targetId;

        // obj output dir: .dotori-cache/obj/<target>-<config>/
        _cacheDir = Path.Combine(
            model.ProjectDir, DotoriConstants.CacheDir,
            DotoriConstants.ObjSubDir, $"{targetId}-{config.ToLower()}");
        Directory.CreateDirectory(_cacheDir);

        // Resolve framework-paths (.framework / .xcframework) into search dirs + names
        ResolveFrameworkPaths();
    }

    /// <summary>
    /// Resolves each entry in <see cref="FlatProjectModel.FrameworkPaths"/> and
    /// populates <see cref="FlatProjectModel.FrameworkSearchPaths"/> (for -F flags)
    /// and appends to <see cref="FlatProjectModel.Frameworks"/> (for -framework flags).
    /// </summary>
    private void ResolveFrameworkPaths()
    {
        foreach (var fwPath in _model.FrameworkPaths)
        {
            var absPath = PathUtils.MakeAbsolute(_model.ProjectDir, fwPath);

            if (fwPath.EndsWith(".xcframework", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = XcframeworkResolver.Resolve(absPath, _targetId);
                if (resolved is null)
                {
                    Console.Error.WriteLine(
                        $"Warning: no matching slice in xcframework for target '{_targetId}': {absPath}");
                    continue;
                }
                _model.FrameworkSearchPaths.Add(resolved.SliceDir);
                _model.Frameworks.Add(resolved.FrameworkName);
            }
            else if (fwPath.EndsWith(".framework", StringComparison.OrdinalIgnoreCase))
            {
                // Direct .framework reference: parent dir is the search path
                var parentDir = Path.GetDirectoryName(absPath) ?? absPath;
                var name      = Path.GetFileNameWithoutExtension(absPath);
                _model.FrameworkSearchPaths.Add(parentDir);
                _model.Frameworks.Add(name);
            }
            else
            {
                // Plain directory: treat as framework search path
                _model.FrameworkSearchPaths.Add(absPath);
            }
        }
    }

    // ─── Source/module glob helpers ───────────────────────────────────────────

    private static bool IsObjcSourceFile(string src)
    {
        var ext = Path.GetExtension(src);
        return ext.Equals(".m",  StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mm", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> ExpandSources() =>
        GlobExpander.Expand(
            _model.ProjectDir,
            _model.Sources.Where(s =>  s.IsInclude).Select(s => s.Glob),
            _model.Sources.Where(s => !s.IsInclude).Select(s => s.Glob));

    private IReadOnlyList<string> ExpandModules() =>
        GlobExpander.Expand(
            _model.ProjectDir,
            _model.Modules.Where(s =>  s.IsInclude).Select(s => s.Glob),
            _model.Modules.Where(s => !s.IsInclude).Select(s => s.Glob));

    // ─── Compile jobs ─────────────────────────────────────────────────────────

    /// <summary>
    /// Plan and return all compile jobs for this project.
    /// Handles unity batching, PCH, and incremental builds.
    /// Does NOT include module BMI compile jobs (use <see cref="PlanModuleJobsAsync"/> for those).
    /// </summary>
    /// <param name="checker">Optional incremental build checker.</param>
    /// <param name="pchPlan">Optional pre-compiled header plan.</param>
    /// <param name="noUnity">If true, bypass unity build batching.</param>
    /// <param name="bmiPaths">Optional map of logical module name → .pcm/.ifc path for import flags.</param>
    public IReadOnlyList<CompileJob> PlanCompileJobs(
        IncrementalChecker? checker = null,
        PchPlanner.PchPlan? pchPlan = null,
        bool                noUnity = false,
        IReadOnlyDictionary<string, string>? bmiPaths = null)
    {
        var allSources    = ExpandSources();
        var moduleSources = ExpandModules();

        List<string> filesToCompile;

        if (!noUnity && _model.UnityBuild?.Enabled == true)
        {
            var unityDir  = Path.Combine(_model.ProjectDir, DotoriConstants.CacheDir, DotoriConstants.UnitySubDir);
            var exclude   = _model.UnityBuild.Exclude;
            int batchSize = _model.UnityBuild.BatchSize > 0 ? _model.UnityBuild.BatchSize : DotoriConstants.DefaultUnityBatchSize;

            var (unityFiles, nonUnity) = UnityBatcher.CreateBatches(
                allSources, moduleSources, exclude, batchSize, unityDir);

            filesToCompile = new List<string>(unityFiles);
            filesToCompile.AddRange(nonUnity);
        }
        else
        {
            // Exclude module sources from regular compile (they go through PlanModuleJobsAsync)
            var moduleSet = new HashSet<string>(moduleSources, StringComparer.OrdinalIgnoreCase);
            filesToCompile = allSources.Where(f => !moduleSet.Contains(f)).ToList();
        }

        return BuildCompileJobs(filesToCompile, checker, pchPlan, bmiPaths);
    }

    private IReadOnlyList<CompileJob> BuildCompileJobs(
        IReadOnlyList<string> files,
        IncrementalChecker? checker,
        PchPlanner.PchPlan? pchPlan,
        IReadOnlyDictionary<string, string>? bmiPaths = null)
    {
        var jobs = new List<CompileJob>();

        if (_toolchain.Kind == CompilerKind.Msvc)
        {
            var baseFlags = MsvcDriver.CompileFlags(_model, _toolchain, _config, _cacheDir);
            if (pchPlan is not null) baseFlags = PchPlanner.AddUseFlags(baseFlags, pchPlan).ToList();
            foreach (var src in files)
            {
                var job = MsvcDriver.MakeCompileJob(src, _cacheDir,
                    AddModuleImportFlags(src, baseFlags, bmiPaths, _toolchain.Kind),
                    _model.ForceCxx);
                AttachDependencies(job);
                if (IsUpToDate(job, checker))
                    continue;
                jobs.Add(job);
            }
        }
        else if (_toolchain.Kind == CompilerKind.Emscripten)
        {
            var baseFlags = EmscriptenDriver.CompileFlags(_model, _cacheDir);
            foreach (var src in files)
            {
                var job = EmscriptenDriver.MakeCompileJob(src, _cacheDir, baseFlags, _model.ForceCxx);
                AttachDependencies(job);
                if (IsUpToDate(job, checker))
                    continue;
                jobs.Add(job);
            }
        }
        else  // Clang
        {
            var baseFlags = ClangDriver.CompileFlags(_model, _toolchain, _config, _cacheDir);
            if (pchPlan is not null) baseFlags = PchPlanner.AddUseFlags(baseFlags, pchPlan).ToList();
            foreach (var src in files)
            {
                var flags = AddModuleImportFlags(src, baseFlags, bmiPaths, _toolchain.Kind);
                if (IsObjcSourceFile(src)) _model.HasObjcSources = true;
                var job = ClangDriver.MakeCompileJob(src, _cacheDir, flags,
                    cAsCpp: _model.ForceCxx,
                    objcArc: _model.ObjcArc,
                    forceObjcpp: _model.ForceObjcpp);
                AttachDependencies(job);
                if (IsUpToDate(job, checker))
                    continue;
                jobs.Add(job);
            }
        }

        return jobs;
    }

    private bool IsUpToDate(CompileJob job, IncrementalChecker? checker) =>
        checker is not null && File.Exists(job.OutputFile) &&
        !checker.IsChanged(job.SourceFile) &&
        !job.DependencyFiles.Any(checker.IsChanged) &&
        !checker.IsFingerprintChanged(job.OutputFile, job.Fingerprint);

    private void AttachDependencies(CompileJob job)
    {
        var searchPaths = _model.Headers
            .Select(header => PathUtils.MakeAbsolute(_model.ProjectDir, header.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        job.DependencyFiles = IncludeScanner.CollectResolvedFiles(
            job.SourceFile, searchPaths, includeSystem: false);
        job.Fingerprint = BuildActionFingerprint.Compute(
            _toolchain.CompilerPath, job.Args, job.DependencyFiles);
    }

    /// <summary>
    /// Scans a source file for module imports and returns flags augmented with
    /// -fmodule-file= (Clang) or /reference (MSVC) for each imported module.
    /// </summary>
    private static IReadOnlyList<string> AddModuleImportFlags(
        string sourceFile,
        IReadOnlyList<string> baseFlags,
        IReadOnlyDictionary<string, string>? bmiPaths,
        CompilerKind kind)
    {
        if (bmiPaths is null || bmiPaths.Count == 0) return baseFlags;

        var dep = ModuleScanner.ScanByText(sourceFile);
        if (dep.Requires.Count == 0) return baseFlags;

        var importFlags = ModuleSorter.BuildImportFlags(dep.Requires, bmiPaths, kind);
        if (importFlags.Count == 0) return baseFlags;

        var combined = new List<string>(baseFlags);
        combined.AddRange(importFlags);
        return combined;
    }

    // ─── PCH planning ─────────────────────────────────────────────────────────

    /// <summary>
    /// Plan the PCH build. Returns null if no PCH is configured.
    /// Returns a plan with <see cref="PchPlanner.PchPlan.BuildJob"/> == null if PCH is up-to-date.
    /// </summary>
    public PchPlanner.PchPlan? PlanPch(IncrementalChecker? checker = null)
    {
        if (_model.Pch is null) return null;

        bool hasModules = _model.Modules.Any(m => m.IsInclude);

        IReadOnlyList<string> baseFlags = _toolchain.Kind == CompilerKind.Msvc
            ? MsvcDriver.CompileFlags(_model, _toolchain, _config, _cacheDir)
            : _toolchain.Kind == CompilerKind.Emscripten
                ? EmscriptenDriver.CompileFlags(_model, _cacheDir)
                : ClangDriver.CompileFlags(_model, _toolchain, _config, _cacheDir);

        return PchPlanner.Plan(_model, _toolchain, baseFlags, checker, hasModules);
    }

    // ─── Module jobs ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a map of logical module name → BMI file path from a list of module compile jobs.
    /// The job's source file is scanned to find the provided module name.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ExtractBmiPaths(
        IReadOnlyList<CompileJob> moduleJobs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var job in moduleJobs)
        {
            var dep = ModuleScanner.ScanByText(job.SourceFile);
            if (dep.Provides is not null)
                map[dep.Provides] = job.OutputFile;
        }
        return map;
    }

    /// <summary>
    /// Scan and sort module files, then produce BMI compile jobs in dependency order.
    /// </summary>
    public async Task<IReadOnlyList<CompileJob>> PlanModuleJobsAsync(
        string? compilerPathOverride = null,
        CancellationToken ct = default)
    {
        var moduleFiles = ExpandModules();

        if (moduleFiles.Count == 0) return Array.Empty<CompileJob>();

        var compilerPath = compilerPathOverride ?? _toolchain.CompilerPath;

        IReadOnlyList<string> compileFlags = _toolchain.Kind == CompilerKind.Msvc
            ? MsvcDriver.CompileFlags(_model, _toolchain, _config, _cacheDir)
            : ClangDriver.CompileFlags(_model, _toolchain, _config, _cacheDir);

        // Scan all module files for dependencies
        var deps = await ModuleScanner.ScanAllAsync(
            compilerPath, _toolchain.Kind, moduleFiles, compileFlags, ct);

        // Topological sort
        var sorted = ModuleSorter.Sort(deps);

        // Build BMI compile jobs
        var jobs = ModuleSorter.BuildModuleJobs(sorted, _cacheDir, _toolchain.Kind, compileFlags);

        return jobs;
    }

    // ─── Module map ───────────────────────────────────────────────────────────

    /// <summary>
    /// Write module-map.json to the BMI directory after module jobs have been compiled.
    /// Only written when <see cref="FlatProjectModel.ModuleExportMap"/> is true.
    /// </summary>
    public void WriteModuleMap(IReadOnlyList<CompileJob> moduleJobs)
    {
        if (!_model.ModuleExportMap) return;
        if (moduleJobs.Count == 0) return;

        var bmiDir = Path.Combine(_cacheDir, DotoriConstants.BmiSubDir);
        ModuleMapWriter.Write(moduleJobs, _targetId, _config, bmiDir);
    }

    // ─── Single-file build support ────────────────────────────────────────────

    /// <summary>
    /// Find the compile job for a specific source file.
    /// If Unity Build is enabled and <paramref name="noUnity"/> is false,
    /// returns the compile job for the unity batch file containing the specified file.
    /// For module files (.cppm/.ixx), returns a BMI-only job (no link implied).
    /// </summary>
    /// <param name="targetFile">Absolute path to the target source file.</param>
    /// <param name="noUnity">If true, skip Unity batch lookup and compile the file directly.</param>
    /// <returns>The matching compile job, or null if the file is not in project sources.</returns>
    public CompileJob? PlanSingleFileJob(string targetFile, bool noUnity = false)
    {
        bool isModule = IsModuleFile(targetFile);

        if (isModule)
        {
            // Module file: return a BMI-only compile job
            IReadOnlyList<string> flags = _toolchain.Kind == CompilerKind.Msvc
                ? MsvcDriver.CompileFlags(_model, _toolchain, _config, _cacheDir)
                : ClangDriver.CompileFlags(_model, _toolchain, _config, _cacheDir);

            return _toolchain.Kind == CompilerKind.Msvc
                ? MakeMsvcModuleJob(targetFile, flags)
                : ClangDriver.MakeCompileJob(targetFile, _cacheDir, flags, isModule: true);
        }

        // Check if file is in project sources
        if (!IsInSources(targetFile)) return null;

        if (!noUnity && _model.UnityBuild?.Enabled == true)
        {
            // Find the unity batch that contains this file
            var unityFile = FindUnityBatchForFile(targetFile);
            if (unityFile is not null)
                return BuildSingleCompileJob(unityFile);
        }

        return BuildSingleCompileJob(targetFile);
    }

    private static bool IsModuleFile(string file)
    {
        var ext = Path.GetExtension(file);
        return ext.Equals(".cppm", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".ixx",  StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInSources(string targetFile)
    {
        return ExpandSources().Concat(ExpandModules())
            .Any(f => string.Equals(f, targetFile, StringComparison.OrdinalIgnoreCase));
    }

    private string? FindUnityBatchForFile(string targetFile)
    {
        var allSources    = ExpandSources();
        var moduleSources = ExpandModules();

        var unityDir  = Path.Combine(_model.ProjectDir, DotoriConstants.CacheDir, DotoriConstants.UnitySubDir);
        var exclude   = _model.UnityBuild!.Exclude;
        int batchSize = _model.UnityBuild.BatchSize > 0 ? _model.UnityBuild.BatchSize : DotoriConstants.DefaultUnityBatchSize;

        var (unityFiles, _) = UnityBatcher.CreateBatches(
            allSources, moduleSources, exclude, batchSize, unityDir);

        var normalizedTarget = targetFile.Replace('\\', '/');
        foreach (var unityFile in unityFiles)
        {
            if (!File.Exists(unityFile)) continue;
            var content = File.ReadAllText(unityFile);
            if (content.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                return unityFile;
        }

        return null;
    }

    private CompileJob BuildSingleCompileJob(string sourceFile)
    {
        if (_toolchain.Kind == CompilerKind.Msvc)
        {
            var flags = MsvcDriver.CompileFlags(_model, _toolchain, _config, _cacheDir);
            return MsvcDriver.MakeCompileJob(sourceFile, _cacheDir, flags, _model.ForceCxx);
        }
        else if (_toolchain.Kind == CompilerKind.Emscripten)
        {
            var flags = EmscriptenDriver.CompileFlags(_model, _cacheDir);
            return EmscriptenDriver.MakeCompileJob(sourceFile, _cacheDir, flags, _model.ForceCxx);
        }
        else
        {
            var flags = ClangDriver.CompileFlags(_model, _toolchain, _config, _cacheDir);
            return ClangDriver.MakeCompileJob(sourceFile, _cacheDir, flags, cAsCpp: _model.ForceCxx);
        }
    }

    private CompileJob MakeMsvcModuleJob(string sourceFile, IReadOnlyList<string> compileFlags)
    {
        var bmiDir = Path.Combine(_model.ProjectDir, DotoriConstants.CacheDir, DotoriConstants.BmiSubDir);
        Directory.CreateDirectory(bmiDir);

        var bmiFile = Path.Combine(bmiDir,
            Path.GetFileNameWithoutExtension(sourceFile) + ".ifc");

        var args = new List<string>(compileFlags);
        args.RemoveAll(f => f.StartsWith("/Fo"));
        args.Remove("/c");
        args.Add("/interface");
        args.Add("/TP");
        args.Add($"\"{sourceFile}\"");
        args.Add($"/ifcOutput \"{bmiFile}\"");

        return new CompileJob
        {
            SourceFile = sourceFile,
            OutputFile = bmiFile,
            Args       = args.ToArray(),
        };
    }
}
