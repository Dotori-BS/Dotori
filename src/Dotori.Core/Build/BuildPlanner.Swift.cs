namespace Dotori.Core.Build;

public sealed partial class BuildPlanner
{
    public CompileJob? PlanSwiftCompileJob(
        IncrementalChecker? checker = null,
        IReadOnlyList<string>? dependencyModuleDirectories = null,
        Action<CompileJob, IReadOnlyList<string>>? explain = null)
    {
        var allSources = ExpandSources();
        var swiftSources = allSources
            .Where(path => Path.GetExtension(path).Equals(".swift", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _model.HasSwiftSources = swiftSources.Length > 0;
        if (swiftSources.Length == 0) return null;

        var compiler = SwiftDriver.FindCompiler()
            ?? throw new InvalidOperationException(
                "Swift sources were found, but swiftc is unavailable. Set SWIFTC or install a Swift toolchain.");
        var hasCxxSources = allSources.Any(path =>
            !Path.GetExtension(path).Equals(".swift", StringComparison.OrdinalIgnoreCase));
        var job = SwiftDriver.MakeCompileJob(
            _model, _toolchain, _config, _cacheDir, swiftSources, compiler,
            hasCxxSources, dependencyModuleDirectories ?? []);
        return NeedsBuild(job, checker, explain) ? job : null;
    }

    public string GetSwiftCompilerPath() => SwiftDriver.FindCompiler()
        ?? throw new InvalidOperationException(
            "Swift sources were found, but swiftc is unavailable. Set SWIFTC or install a Swift toolchain.");
}
