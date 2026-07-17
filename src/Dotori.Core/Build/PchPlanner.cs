using Dotori.Core;
using Dotori.Core.Model;
using Dotori.Core.Toolchain;

namespace Dotori.Core.Build;

/// <summary>
/// Plans Precompiled Header (PCH) compilation.
///
/// MSVC:
///   - Create:  cl.exe /Yc"pch.h" /Fp"pch.pch" pch.cpp
///   - Use:     cl.exe /Yu"pch.h" /Fp"pch.pch" ...
///
/// Clang:
///   - Create:  clang++ -x c++-header pch.h -o pch.h.pch
///   - Use:     clang++ -include-pch pch.h.pch ...
///
/// Warning: using PCH and C++ Modules simultaneously is not recommended.
/// When both are configured, a warning is emitted and PCH is skipped for
/// module files.
/// </summary>
public static class PchPlanner
{
    /// <summary>The result of planning PCH compilation.</summary>
    public sealed class PchPlan
    {
        /// <summary>Compile job to build the PCH (null if already up-to-date).</summary>
        public CompileJob? BuildJob  { get; init; }
        /// <summary>The generated PCH output file path (e.g. .pch or .gch).</summary>
        public required string PchFile { get; init; }
        /// <summary>Additional compiler flags to add to all compile jobs using this PCH.</summary>
        public required IReadOnlyList<string> UseFlags { get; init; }
        /// <summary>Whether PCH was already up-to-date (no build needed).</summary>
        public bool IsUpToDate { get; init; }
    }

    /// <summary>
    /// Plan the PCH build for a project.
    /// </summary>
    /// <param name="model">Flat project model containing PCH config.</param>
    /// <param name="toolchain">Detected toolchain.</param>
    /// <param name="compileFlags">Base compile flags (without PCH-specific ones).</param>
    /// <param name="checker">Incremental checker to detect stale PCH.</param>
    /// <param name="hasModules">If true, emit a warning and reduce PCH scope.</param>
    /// <returns>The PCH plan, or null if no PCH is configured.</returns>
    public static PchPlan? Plan(
        FlatProjectModel model,
        ToolchainInfo toolchain,
        IReadOnlyList<string> compileFlags,
        IncrementalChecker? checker = null,
        bool hasModules = false)
    {
        if (model.Pch is null) return null;

        var pchConfig = model.Pch;

        if (pchConfig.Header is null)
            return null; // No header configured — nothing to do

        if (hasModules && pchConfig.Modules != true)
        {
            // PCH + Modules warning (SPEC: emit warning, PCH applies to non-module files only)
            Console.Error.WriteLine(
                "Warning: PCH and C++ Modules are both enabled. " +
                "PCH will not apply to module interface files (.cppm/.ixx).");
        }

        var pchHeaderPath = Path.GetFullPath(
            Path.Combine(model.ProjectDir, pchConfig.Header));

        if (!File.Exists(pchHeaderPath))
        {
            Console.Error.WriteLine(
                $"Warning: PCH header '{pchHeaderPath}' not found, skipping PCH.");
            return null;
        }

        if (toolchain.Kind == CompilerKind.Msvc)
            return PlanMsvc(model, toolchain, compileFlags, checker, pchHeaderPath, pchConfig.Source);
        else
            return PlanClang(model, toolchain, compileFlags, checker, pchHeaderPath);
    }

    // ─── MSVC PCH ────────────────────────────────────────────────────────────

    private static PchPlan PlanMsvc(
        FlatProjectModel model,
        ToolchainInfo toolchain,
        IReadOnlyList<string> compileFlags,
        IncrementalChecker? checker,
        string pchHeader,
        string? pchSource)
    {
        var pchDir  = Path.Combine(model.ProjectDir, DotoriConstants.CacheDir, DotoriConstants.PchSubDir);
        Directory.CreateDirectory(pchDir);

        var pchFile = Path.Combine(pchDir, Path.GetFileName(pchHeader) + ".pch");
        var headerName = Path.GetFileName(pchHeader);

        // Use flags: /Yu"pch.h" /Fp"path/pch.pch"
        var useFlags = new List<string>
        {
            $"/Yu\"{headerName}\"",
            $"/Fp\"{pchFile}\"",
        };

        // Check if PCH is up-to-date
        bool upToDate = File.Exists(pchFile) &&
                        (checker is null || !checker.IsChanged(pchHeader));

        if (upToDate)
            return new PchPlan { PchFile = pchFile, UseFlags = useFlags, IsUpToDate = true };

        // Build the PCH from the source file (or from the header directly if no source)
        var buildSource = pchSource is not null
            ? Path.GetFullPath(Path.Combine(model.ProjectDir, pchSource))
            : pchHeader;

        var buildArgs = new List<string>(compileFlags);
        buildArgs.RemoveAll(f => f.StartsWith("/Yu"));  // No /Yu when creating
        buildArgs.Add($"/Yc\"{headerName}\"");
        buildArgs.Add($"/Fp\"{pchFile}\"");
        buildArgs.Add($"\"{buildSource}\"");

        var objFile = BuildOutputPath.ForSource(buildSource, pchDir, ".obj");
        buildArgs.Add($"/Fo\"{objFile}\"");

        var job = new CompileJob
        {
            SourceFile = buildSource,
            OutputFile = objFile,
            Args       = buildArgs.ToArray(),
        };

        return new PchPlan { BuildJob = job, PchFile = pchFile, UseFlags = useFlags };
    }

    // ─── Clang PCH ───────────────────────────────────────────────────────────

    private static PchPlan PlanClang(
        FlatProjectModel model,
        ToolchainInfo toolchain,
        IReadOnlyList<string> compileFlags,
        IncrementalChecker? checker,
        string pchHeader)
    {
        var pchDir  = Path.Combine(model.ProjectDir, DotoriConstants.CacheDir, DotoriConstants.PchSubDir);
        Directory.CreateDirectory(pchDir);

        var pchFile = Path.Combine(pchDir, Path.GetFileName(pchHeader) + ".pch");

        // Use flags: -include-pch "path/pch.h.pch"
        var useFlags = new List<string> { $"-include-pch \"{pchFile}\"" };

        // Check if PCH is up-to-date
        bool upToDate = File.Exists(pchFile) &&
                        (checker is null || !checker.IsChanged(pchHeader));

        if (upToDate)
            return new PchPlan { PchFile = pchFile, UseFlags = useFlags, IsUpToDate = true };

        // Build PCH: clang++ -x c++-header pch.h -o pch.h.pch
        var buildArgs = new List<string>(compileFlags);
        buildArgs.Remove("-c");  // No -c when generating PCH
        buildArgs.Add("-x c++-header");
        buildArgs.Add($"\"{pchHeader}\"");
        buildArgs.Add($"-o \"{pchFile}\"");

        var job = new CompileJob
        {
            SourceFile = pchHeader,
            OutputFile = pchFile,
            Args       = buildArgs.ToArray(),
        };

        return new PchPlan { BuildJob = job, PchFile = pchFile, UseFlags = useFlags };
    }

    /// <summary>
    /// Add PCH use flags to an existing compile flags list.
    /// Returns a new list with the PCH flags appended.
    /// </summary>
    public static IReadOnlyList<string> AddUseFlags(
        IReadOnlyList<string> baseFlags,
        PchPlan plan)
    {
        var merged = new List<string>(baseFlags);
        merged.AddRange(plan.UseFlags);
        return merged;
    }
}
