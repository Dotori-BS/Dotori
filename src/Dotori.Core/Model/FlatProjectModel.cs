using Dotori.Core.Parsing;

namespace Dotori.Core.Model;

/// <summary>
/// Flattened project configuration after merging all applicable condition blocks
/// for a given <see cref="TargetContext"/>.
/// Condition specificity rule: more-specific conditions override less-specific ones.
/// e.g. [windows.release] > [windows] > (top-level)
/// </summary>
public sealed class FlatProjectModel
{
    public required string Name        { get; init; }
    public required string ProjectDir  { get; init; }  // directory containing .dotori
    public required string DotoriPath  { get; init; }  // absolute path to .dotori file

    // Core props
    public ProjectType   Type         { get; set; } = ProjectType.Executable;
    public CxxStd        Std          { get; set; } = CxxStd.Cxx23;
    public string?       Description  { get; set; }
    public OptimizeLevel Optimize     { get; set; } = OptimizeLevel.None;
    public DebugInfoLevel DebugInfo   { get; set; } = DebugInfoLevel.None;
    public RuntimeLink   RuntimeLink  { get; set; } = RuntimeLink.Static;
    public LibcType?     Libc         { get; set; }
    public StdlibType?   Stdlib       { get; set; }
    public bool          Lto          { get; set; } = false;
    public WarningLevel  Warnings     { get; set; } = WarningLevel.Default;
    public bool          WarningsAsErrors { get; set; } = false;
    public int?          AndroidApiLevel  { get; set; }
    public string?       MacosMin     { get; set; }
    public string?       IosMin       { get; set; }
    public string?       TvosMin      { get; set; }
    public string?       WatchosMin   { get; set; }
    public bool          ForceCxx     { get; set; } = false;
    public bool          ObjcArc      { get; set; } = false;
    public bool          ForceObjcpp  { get; set; } = false;
    /// <summary>
    /// Set by BuildPlanner when .m or .mm source files are present.
    /// Used by AppleLinker to automatically link -lobjc.
    /// </summary>
    public bool          HasObjcSources { get; set; } = false;
    public List<string>  EmscriptenFlags { get; } = new();

    // Accumulated lists (merged across all applicable condition blocks)
    public List<SourceItem>     Sources      { get; } = new();
    public List<SourceItem>     Modules      { get; } = new();
    public List<HeaderItem>     Headers      { get; } = new();
    public List<string>         Defines      { get; } = new();
    public List<string>         Links        { get; } = new();
    public List<string>         Frameworks   { get; } = new();
    public List<string>         CompileFlags { get; } = new();
    public List<string>         LinkFlags    { get; } = new();
    public List<DependencyItem> Dependencies { get; } = new();

    // Apple-specific: custom framework/xcframework paths
    /// <summary>
    /// DSL-declared paths to .framework or .xcframework bundles.
    /// Resolved at build time by BuildPlanner into FrameworkSearchPaths + Frameworks entries.
    /// </summary>
    public List<string> FrameworkPaths       { get; } = new();
    /// <summary>
    /// Resolved framework search directories (-F flags).
    /// Populated by BuildPlanner.ResolveFrameworkPaths() from FrameworkPaths.
    /// </summary>
    public List<string> FrameworkSearchPaths { get; } = new();

    // Windows-specific: resource and manifest files
    public List<string> Resources { get; } = new();  // .rc file paths → compiled to .res by rc.exe
    public string?      Manifest  { get; set; }      // .manifest path → embedded by mt.exe after link

    // Named toolchain override: "apple-clang", "brew-llvm", "mise-llvm", "msvc-2022", etc.
    // Equivalent to --compiler on the CLI; CLI flag takes precedence if both are set.
    public string? Toolchain { get; set; }

    // Optional blocks (last one wins)
    public PchConfig?         Pch         { get; set; }
    public UnityBuildConfig?  UnityBuild  { get; set; }
    public OutputConfig?      Output      { get; set; }
    public AssemblerConfig?   Assembler   { get; set; }

    // Build scripts (accumulated across all applicable condition blocks)
    public List<string> PreBuildCommands  { get; } = new();
    public List<string> PostBuildCommands { get; } = new();
    public List<BuildRuleConfig> BuildRules { get; } = new();

    // File copy items (accumulated across all applicable condition blocks)
    public List<CopyItem> CopyItems { get; } = new();

    // Module export map generation (default: true when modules are present)
    public bool ModuleExportMap { get; set; } = true;
}

public sealed record BuildRuleConfig(
    string Name, string Command, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs);

public sealed class PchConfig
{
    public string? Header  { get; set; }
    public string? Source  { get; set; }
    public bool?   Modules { get; set; }
}

public sealed class UnityBuildConfig
{
    public bool        Enabled   { get; set; } = false;
    public int         BatchSize { get; set; } = 8;
    public List<string> Exclude  { get; } = new();
}

public sealed class OutputConfig
{
    /// <summary>Target directory for executables and shared libraries (dll/so/dylib). Relative to project root.</summary>
    public string? Binaries  { get; set; }
    /// <summary>Target directory for static libraries (.a) and Windows import libraries (.lib). Relative to project root.</summary>
    public string? Libraries { get; set; }
    /// <summary>Target directory for debug symbols (.pdb, .dSYM). Relative to project root.</summary>
    public string? Symbols   { get; set; }
}

public sealed class AssemblerConfig
{
    public AssemblerTool    Tool    { get; set; } = AssemblerTool.Auto;
    public string?          Format  { get; set; }
    public List<SourceItem> Items   { get; } = new();
    public List<string>     Flags   { get; } = new();
    public List<string>     Defines { get; } = new();
}
