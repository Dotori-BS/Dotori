namespace Dotori.Core.Parsing;

// ─── Source location ───────────────────────────────────────────────────────

public readonly record struct SourceLocation(string File, int Line, int Column)
{
    public override string ToString() => $"{File}:{Line}:{Column}";
}

// ─── Top-level ─────────────────────────────────────────────────────────────

public sealed class DotoriFile
{
    public required string FilePath { get; init; }
    public ProjectDecl? Project { get; init; }
    public PackageDecl? Package { get; init; }
    /// <summary>Comments that appear after the last top-level declaration (trailing).</summary>
    public List<string> TrailingComments { get; } = new();
}

// ─── Project declaration ───────────────────────────────────────────────────

public sealed class ProjectDecl
{
    public required SourceLocation Location { get; init; }
    public required string Name { get; init; }
    public List<ProjectItem> Items { get; } = new();
    /// <summary>Comments that appear before the <c>project</c> keyword.</summary>
    public List<string> LeadingComments { get; } = new();
}

/// <summary>Base class for all items that can appear inside a project block.</summary>
public abstract class ProjectItem
{
    public required SourceLocation Location { get; init; }
}

/// <summary>A <c># comment</c> line inside a project or condition block.</summary>
public sealed class CommentItem(string text) : ProjectItem
{
    public string Text { get; } = text;
}

// Props
public sealed class ProjectTypeProp(ProjectType value) : ProjectItem
{
    public ProjectType Value { get; } = value;
}
public sealed class StdProp(CxxStd value) : ProjectItem
{
    public CxxStd Value { get; } = value;
}
public sealed class DescriptionProp(string value) : ProjectItem
{
    public string Value { get; } = value;
}
public sealed class OptimizeProp(OptimizeLevel value) : ProjectItem
{
    public OptimizeLevel Value { get; } = value;
}
public sealed class DebugInfoProp(DebugInfoLevel value) : ProjectItem
{
    public DebugInfoLevel Value { get; } = value;
}
public sealed class RuntimeLinkProp(RuntimeLink value) : ProjectItem
{
    public RuntimeLink Value { get; } = value;
}
public sealed class LibcProp(LibcType value) : ProjectItem
{
    public LibcType Value { get; } = value;
}
public sealed class StdlibProp(StdlibType value) : ProjectItem
{
    public StdlibType Value { get; } = value;
}
public sealed class LtoProp(bool value) : ProjectItem
{
    public bool Value { get; } = value;
}
public sealed class WarningsProp(WarningLevel value) : ProjectItem
{
    public WarningLevel Value { get; } = value;
}
public sealed class WarningsAsErrorsProp(bool value) : ProjectItem
{
    public bool Value { get; } = value;
}
public sealed class AndroidApiLevelProp(int value) : ProjectItem
{
    public int Value { get; } = value;
}
public sealed class MacosMinProp(string value) : ProjectItem
{
    public string Value { get; } = value;
}
public sealed class IosMinProp(string value) : ProjectItem
{
    public string Value { get; } = value;
}
public sealed class TvosMinProp(string value) : ProjectItem
{
    public string Value { get; } = value;
}
public sealed class WatchosMinProp(string value) : ProjectItem
{
    public string Value { get; } = value;
}
public sealed class ForceCxxProp(bool value) : ProjectItem
{
    public bool Value { get; } = value;
}
public sealed class ObjcArcProp(bool value) : ProjectItem
{
    public bool Value { get; } = value;
}
public sealed class ForceObjcppProp(bool value) : ProjectItem
{
    public bool Value { get; } = value;
}
public sealed class EmscriptenFlagsProp(IReadOnlyList<string> flags) : ProjectItem
{
    public IReadOnlyList<string> Flags { get; } = flags;
}

// Blocks
public sealed class SourcesBlock(bool isModules) : ProjectItem
{
    public bool IsModules { get; } = isModules;
    public List<SourceItem> Items { get; } = new();
    /// <summary>Only meaningful when IsModules=true. null = inherit default (true).</summary>
    public bool? ExportMap { get; set; }
}
public sealed class SourceItem(bool isInclude, string glob)
{
    public bool IsInclude { get; } = isInclude;
    public string Glob { get; } = glob;
}

public sealed class HeadersBlock : ProjectItem
{
    public List<HeaderItem> Items { get; } = new();
}
public sealed class HeaderItem(bool isPublic, string path)
{
    public bool IsPublic { get; } = isPublic;
    public string Path { get; } = path;
}

/// <summary>
/// Marker interface for AST blocks that hold a flat string-value list.
/// Enables a single generic parser helper instead of one method per block type.
/// </summary>
public interface IStringValuesBlock
{
    List<string> Values { get; }
}

public sealed class DefinesBlock : ProjectItem, IStringValuesBlock
{
    public List<string> Values { get; } = new();
}
public sealed class LinksBlock : ProjectItem, IStringValuesBlock
{
    public List<string> Values { get; } = new();
}
public sealed class FrameworksBlock : ProjectItem, IStringValuesBlock
{
    public List<string> Values { get; } = new();
}

/// <summary>
/// Paths to .framework or .xcframework bundles (Apple only).
/// At build time each entry is resolved to a framework search dir (-F)
/// and framework name (-framework), then merged into the model.
/// </summary>
public sealed class FrameworkPathsBlock : ProjectItem
{
    public List<string> Paths { get; } = new();
}

public sealed class CompileFlagsBlock : ProjectItem, IStringValuesBlock
{
    public List<string> Values { get; } = new();
}
public sealed class LinkFlagsBlock : ProjectItem, IStringValuesBlock
{
    public List<string> Values { get; } = new();
}

/// <summary>Windows resource files (.rc) to compile with rc.exe.</summary>
public sealed class ResourcesBlock : ProjectItem
{
    public List<string> Paths { get; } = new();
}

/// <summary>Windows application manifest file (.manifest) to embed after linking.</summary>
public sealed class ManifestProp(string value) : ProjectItem
{
    public string Value { get; } = value;
}

/// <summary>
/// Named toolchain preference: "apple-clang", "brew-llvm", "mise-llvm",
/// "clang", "msvc", "msvc-2022", "msvc-2019", "msvc-2017",
/// or an explicit compiler binary name / path.
/// Equivalent to --compiler on the CLI.
/// </summary>
public sealed class ToolchainProp(string value) : ProjectItem
{
    public string Value { get; } = value;
}

public enum AssemblerTool
{
    Auto,
    Nasm,
    Yasm,
    Gas,
    Masm,
}

/// <summary>
/// External assembler block — compiles .asm/.s/.S files with NASM, YASM, GAS, or MASM.
/// Supports platform-specific configuration via condition blocks.
/// </summary>
public sealed class AssemblerBlock : ProjectItem
{
    public AssemblerTool Tool { get; set; } = AssemblerTool.Auto;
    public string? Format { get; set; }
    public List<SourceItem> Items { get; } = new();
    public List<string> Flags { get; } = new();
    public List<string> Defines { get; } = new();
}

public sealed class DependenciesBlock : ProjectItem
{
    public List<DependencyItem> Items { get; } = new();
}
public sealed class DependencyItem(string name, DependencyValue value)
{
    public string Name { get; } = name;
    public DependencyValue Value { get; } = value;
}
public abstract class DependencyValue { }
public sealed class VersionDependency(string version) : DependencyValue
{
    public string Version { get; } = version;
}
public sealed class ComplexDependency : DependencyValue
{
    public string? Git { get; set; }
    public string? Tag { get; set; }
    public string? Commit { get; set; }
    public string? Path { get; set; }
    public string? Version { get; set; }
    /// <summary>
    /// If set, this dependency is only included when ALL named options are active.
    /// </summary>
    public List<string>? Options { get; set; }
}

public sealed class PchBlock : ProjectItem
{
    public string? Header { get; set; }
    public string? Source { get; set; }
    public bool? Modules { get; set; }
}

public sealed class UnityBuildBlock : ProjectItem
{
    public bool? Enabled { get; set; }
    public int? BatchSize { get; set; }
    public List<string> Exclude { get; } = new();
}

public sealed class OutputBlock : ProjectItem
{
    public string? Binaries  { get; set; }  // exe, dll/so/dylib copy dir
    public string? Libraries { get; set; }  // .lib/.a copy dir
    public string? Symbols   { get; set; }  // .pdb/.dSYM copy dir
}

public sealed class PreBuildBlock : ProjectItem
{
    public List<string> Commands { get; } = new();
}

public sealed class PostBuildBlock : ProjectItem
{
    public List<string> Commands { get; } = new();
}

/// <summary>A named incremental custom command with declared inputs and outputs.</summary>
public sealed class BuildRuleBlock(string name) : ProjectItem
{
    public string Name { get; } = name;
    public string? Command { get; set; }
    public List<string> Inputs { get; } = new();
    public List<string> Outputs { get; } = new();
}

public sealed class CopyItem(string from, string to)
{
    public string From { get; } = from;
    public string To   { get; } = to;
}

public sealed class CopyBlock : ProjectItem
{
    public List<CopyItem> Items { get; } = new();
}

public sealed class ConditionBlock(ConditionExpr condition) : ProjectItem
{
    public ConditionExpr Condition { get; } = condition;
    public List<ProjectItem> Items { get; } = new();
}

/// <summary>
/// Declares a named build option with a default value, optional defines, and optional dependencies.
/// When the option is active its defines and dependencies are merged into the flat model,
/// and its name is added to the active-atom set so <c>[option-name]</c> condition blocks fire.
/// </summary>
public sealed class OptionBlock(string name, bool defaultEnabled) : ProjectItem
{
    public string Name { get; } = name;
    public bool Default { get; } = defaultEnabled;
    public List<string> Defines { get; } = new();
    public List<DependencyItem> Dependencies { get; } = new();
}

// ─── Conditions ────────────────────────────────────────────────────────────

/// <summary>Condition is a dot-joined sequence of atoms, e.g. [windows.release].</summary>
public sealed class ConditionExpr(IReadOnlyList<string> atoms)
{
    public IReadOnlyList<string> Atoms { get; } = atoms;

    public override string ToString() => string.Join(".", Atoms);

    /// <summary>Specificity = number of atoms (more atoms = higher priority).</summary>
    public int Specificity => Atoms.Count;
}

// ─── Package declaration ───────────────────────────────────────────────────

/// <summary>Base class for items inside a <c>package { }</c> block (for formatter use).</summary>
public abstract class PackageBodyItem
{
    public required SourceLocation Location { get; init; }
}

/// <summary>A named field inside a <c>package { }</c> block (e.g. <c>name</c>, <c>version</c>).</summary>
public sealed class PackageFieldItem(string fieldName) : PackageBodyItem
{
    public string FieldName { get; } = fieldName;
}

/// <summary>A <c># comment</c> line inside a <c>package { }</c> block.</summary>
public sealed class PackageCommentItem(string text) : PackageBodyItem
{
    public string Text { get; } = text;
}

public sealed class PackageDecl
{
    public required SourceLocation Location { get; init; }
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? License { get; set; }
    public string? Homepage { get; set; }
    public List<string> Authors { get; } = new();
    public Dictionary<string, string> Exports { get; } = new();
    /// <summary>Comments that appear before the <c>package</c> keyword.</summary>
    public List<string> LeadingComments { get; } = new();
    /// <summary>
    /// Ordered list of fields and comments inside the block, for formatter use.
    /// Populated by the parser; empty when constructed programmatically.
    /// </summary>
    public List<PackageBodyItem> BodyItems { get; } = new();
}

// ─── Enums ─────────────────────────────────────────────────────────────────

public enum ProjectType { Executable, StaticLibrary, SharedLibrary, HeaderOnly }
public enum CxxStd { Cxx17, Cxx20, Cxx23 }
public enum OptimizeLevel { None, Size, Speed, Full }
public enum DebugInfoLevel { None, Minimal, Full }
public enum RuntimeLink { Static, Dynamic }
public enum LibcType { Glibc, Musl }
public enum StdlibType { LibCxx, LibStdCxx }
public enum WarningLevel { None, Default, All, Extra }
