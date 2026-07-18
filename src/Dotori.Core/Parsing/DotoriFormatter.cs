using System.Text;

namespace Dotori.Core.Parsing;

/// <summary>
/// Formats a parsed <see cref="DotoriFile"/> AST back into canonical .dotori source text.
/// Uses <c>#</c> for comments regardless of the original comment syntax.
/// </summary>
public static class DotoriFormatter
{
    /// <summary>
    /// Formats the given <see cref="DotoriFile"/> and returns the canonical source text.
    /// </summary>
    public static string Format(DotoriFile file)
    {
        var sb = new StringBuilder();
        bool hasContent = false;

        if (file.Project is not null)
        {
            FormatProject(sb, file.Project);
            hasContent = true;
        }

        if (file.Package is not null)
        {
            if (hasContent) sb.AppendLine();
            FormatPackage(sb, file.Package);
            hasContent = true;
        }

        if (file.TrailingComments.Count > 0)
        {
            if (hasContent) sb.AppendLine();
            foreach (var c in file.TrailingComments)
                sb.AppendLine($"# {c}");
        }

        return sb.ToString();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static string I(int level) => new string(' ', level * 4);

    private static string QuoteString(string s)
    {
        var escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    /// <summary>
    /// Returns true if <paramref name="item"/> is a simple key=value property
    /// (not a block, not a comment, not a condition).
    /// </summary>
    private static bool IsProp(ProjectItem item) => item is
        ProjectTypeProp or StdProp or DescriptionProp or OptimizeProp or
        DebugInfoProp or RuntimeLinkProp or LibcProp or StdlibProp or
        LtoProp or WarningsProp or WarningsAsErrorsProp or
        AndroidApiLevelProp or MacosMinProp or IosMinProp or
        TvosMinProp or WatchosMinProp or ManifestProp or ToolchainProp or
        EmscriptenFlagsProp;

    // ─── project block ──────────────────────────────────────────────────────

    private static void FormatProject(StringBuilder sb, ProjectDecl project)
    {
        foreach (var c in project.LeadingComments)
            sb.AppendLine($"# {c}");

        sb.AppendLine($"project {project.Name} {{");
        AppendProjectItems(sb, project.Items, 1);
        sb.AppendLine("}");
    }

    private static void AppendProjectItems(StringBuilder sb, List<ProjectItem> items, int indent)
    {
        int i          = 0;
        bool firstGroup = true;

        while (i < items.Count)
        {
            // Collect consecutive CommentItems that precede the next real item
            var comments = new List<string>();
            while (i < items.Count && items[i] is CommentItem c)
            {
                comments.Add(c.Text);
                i++;
            }

            // Peek at the next real item (if any)
            ProjectItem? realItem = i < items.Count ? items[i] : null;

            // ── Blank line logic ─────────────────────────────────────────────
            // No blank line is inserted before the very first group.
            // A blank line is inserted before any group that has leading comments
            // OR whose real item is a non-prop (block/condition).
            // Consecutive props without leading comments are grouped together.
            if (!firstGroup)
            {
                bool nextIsPropWithNoComments = realItem is not null && IsProp(realItem) && comments.Count == 0;
                if (!nextIsPropWithNoComments)
                    sb.AppendLine();
            }

            // Emit leading comments
            foreach (var comment in comments)
                sb.AppendLine($"{I(indent)}# {comment}");

            // Emit the real item (if any)
            if (realItem is not null)
            {
                FormatProjectItem(sb, realItem, indent);
                i++;
            }

            firstGroup = false;
        }
    }

    private static void FormatProjectItem(StringBuilder sb, ProjectItem item, int indent)
    {
        switch (item)
        {
            case CommentItem c:
                sb.AppendLine($"{I(indent)}# {c.Text}");
                break;
            case ProjectTypeProp p:
                sb.AppendLine($"{I(indent)}type = {FormatProjectType(p.Value)}");
                break;
            case StdProp p:
                sb.AppendLine($"{I(indent)}std = {FormatCxxStd(p.Value)}");
                break;
            case DescriptionProp p:
                sb.AppendLine($"{I(indent)}description = {QuoteString(p.Value)}");
                break;
            case OptimizeProp p:
                sb.AppendLine($"{I(indent)}optimize = {FormatOptimize(p.Value)}");
                break;
            case DebugInfoProp p:
                sb.AppendLine($"{I(indent)}debug-info = {FormatDebugInfo(p.Value)}");
                break;
            case RuntimeLinkProp p:
                sb.AppendLine($"{I(indent)}runtime-link = {FormatRuntimeLink(p.Value)}");
                break;
            case LibcProp p:
                sb.AppendLine($"{I(indent)}libc = {FormatLibc(p.Value)}");
                break;
            case StdlibProp p:
                sb.AppendLine($"{I(indent)}stdlib = {FormatStdlib(p.Value)}");
                break;
            case LtoProp p:
                sb.AppendLine($"{I(indent)}lto = {(p.Value ? "true" : "false")}");
                break;
            case WarningsProp p:
                sb.AppendLine($"{I(indent)}warnings = {FormatWarnings(p.Value)}");
                break;
            case WarningsAsErrorsProp p:
                sb.AppendLine($"{I(indent)}warnings-as-errors = {(p.Value ? "true" : "false")}");
                break;
            case AndroidApiLevelProp p:
                sb.AppendLine($"{I(indent)}android-api-level = {p.Value}");
                break;
            case MacosMinProp p:
                sb.AppendLine($"{I(indent)}macos-min = {QuoteString(p.Value)}");
                break;
            case IosMinProp p:
                sb.AppendLine($"{I(indent)}ios-min = {QuoteString(p.Value)}");
                break;
            case TvosMinProp p:
                sb.AppendLine($"{I(indent)}tvos-min = {QuoteString(p.Value)}");
                break;
            case WatchosMinProp p:
                sb.AppendLine($"{I(indent)}watchos-min = {QuoteString(p.Value)}");
                break;
            case ManifestProp p:
                sb.AppendLine($"{I(indent)}manifest = {QuoteString(p.Value)}");
                break;
            case ToolchainProp p:
                sb.AppendLine($"{I(indent)}toolchain = {QuoteString(p.Value)}");
                break;
            case EmscriptenFlagsProp p:
                sb.AppendLine($"{I(indent)}emscripten-flags {{");
                foreach (var f in p.Flags)
                    sb.AppendLine($"{I(indent + 1)}{QuoteString(f)}");
                sb.AppendLine($"{I(indent)}}}");
                break;
            case SourcesBlock b:
                FormatSourcesBlock(sb, b, indent);
                break;
            case HeadersBlock b:
                FormatHeadersBlock(sb, b, indent);
                break;
            case DefinesBlock b:
                FormatStringValuesBlock(sb, "defines", b.Values, indent);
                break;
            case LinksBlock b:
                FormatStringValuesBlock(sb, "links", b.Values, indent);
                break;
            case FrameworksBlock b:
                FormatStringValuesBlock(sb, "frameworks", b.Values, indent);
                break;
            case FrameworkPathsBlock b:
                FormatStringListBlock(sb, "framework-paths", b.Paths, indent);
                break;
            case CompileFlagsBlock b:
                FormatStringValuesBlock(sb, "compile-flags", b.Values, indent);
                break;
            case LinkFlagsBlock b:
                FormatStringValuesBlock(sb, "link-flags", b.Values, indent);
                break;
            case ResourcesBlock b:
                FormatStringListBlock(sb, "resources", b.Paths, indent);
                break;
            case DependenciesBlock b:
                FormatDependenciesBlock(sb, b, indent);
                break;
            case PchBlock b:
                FormatPchBlock(sb, b, indent);
                break;
            case UnityBuildBlock b:
                FormatUnityBuildBlock(sb, b, indent);
                break;
            case OutputBlock b:
                FormatOutputBlock(sb, b, indent);
                break;
            case PreBuildBlock b:
                FormatStringValuesBlock(sb, "pre-build", b.Commands, indent);
                break;
            case PostBuildBlock b:
                FormatStringValuesBlock(sb, "post-build", b.Commands, indent);
                break;
            case BuildRuleBlock b:
                FormatBuildRuleBlock(sb, b, indent);
                break;
            case CopyBlock b:
                FormatCopyBlock(sb, b, indent);
                break;
            case ConditionBlock b:
                FormatConditionBlock(sb, b, indent);
                break;
            case OptionBlock b:
                FormatOptionBlock(sb, b, indent);
                break;
            case AssemblerBlock b:
                FormatAssemblerBlock(sb, b, indent);
                break;
        }
    }

    // ─── Individual block formatters ─────────────────────────────────────────

    private static void FormatSourcesBlock(StringBuilder sb, SourcesBlock block, int indent)
    {
        string name = block.IsModules ? "modules" : "sources";
        sb.AppendLine($"{I(indent)}{name} {{");
        foreach (var item in block.Items)
        {
            string keyword = item.IsInclude ? "include" : "exclude";
            sb.AppendLine($"{I(indent + 1)}{keyword} {QuoteString(item.Glob)}");
        }
        if (block.IsModules && block.ExportMap.HasValue)
            sb.AppendLine($"{I(indent + 1)}export-map = {(block.ExportMap.Value ? "true" : "false")}");
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatHeadersBlock(StringBuilder sb, HeadersBlock block, int indent)
    {
        sb.AppendLine($"{I(indent)}headers {{");
        foreach (var item in block.Items)
        {
            string keyword = item.IsPublic ? "public" : "private";
            sb.AppendLine($"{I(indent + 1)}{keyword} {QuoteString(item.Path)}");
        }
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatStringValuesBlock(StringBuilder sb, string name, IReadOnlyList<string> values, int indent)
    {
        sb.AppendLine($"{I(indent)}{name} {{");
        foreach (var v in values)
            sb.AppendLine($"{I(indent + 1)}{QuoteString(v)}");
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatStringListBlock(StringBuilder sb, string name, IReadOnlyList<string> paths, int indent)
    {
        sb.AppendLine($"{I(indent)}{name} {{");
        foreach (var p in paths)
            sb.AppendLine($"{I(indent + 1)}{QuoteString(p)}");
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatDependenciesBlock(StringBuilder sb, DependenciesBlock block, int indent)
    {
        sb.AppendLine($"{I(indent)}dependencies {{");
        foreach (var dep in block.Items)
        {
            switch (dep.Value)
            {
                case VersionDependency v:
                    sb.AppendLine($"{I(indent + 1)}{dep.Name} = {QuoteString(v.Version)}");
                    break;
                case ComplexDependency c:
                    var parts = new List<string>();
                    if (c.Git     is not null) parts.Add($"git = {QuoteString(c.Git)}");
                    if (c.Tag     is not null) parts.Add($"tag = {QuoteString(c.Tag)}");
                    if (c.Commit  is not null) parts.Add($"commit = {QuoteString(c.Commit)}");
                    if (c.Path    is not null) parts.Add($"path = {QuoteString(c.Path)}");
                    if (c.Version is not null) parts.Add($"version = {QuoteString(c.Version)}");
                    if (c.Options is { Count: 1 }) parts.Add($"option = {QuoteString(c.Options[0])}");
                    else if (c.Options is { Count: > 1 }) parts.Add($"option = {{ {string.Join(" ", c.Options.Select(QuoteString))} }}");
                    sb.AppendLine($"{I(indent + 1)}{dep.Name} = {{ {string.Join(", ", parts)} }}");
                    break;
            }
        }
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatPchBlock(StringBuilder sb, PchBlock block, int indent)
    {
        sb.AppendLine($"{I(indent)}pch {{");
        if (block.Header  is not null) sb.AppendLine($"{I(indent + 1)}header = {QuoteString(block.Header)}");
        if (block.Source  is not null) sb.AppendLine($"{I(indent + 1)}source = {QuoteString(block.Source)}");
        if (block.Modules.HasValue)    sb.AppendLine($"{I(indent + 1)}modules = {(block.Modules.Value ? "true" : "false")}");
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatUnityBuildBlock(StringBuilder sb, UnityBuildBlock block, int indent)
    {
        sb.AppendLine($"{I(indent)}unity-build {{");
        if (block.Enabled.HasValue)   sb.AppendLine($"{I(indent + 1)}enabled = {(block.Enabled.Value ? "true" : "false")}");
        if (block.BatchSize.HasValue) sb.AppendLine($"{I(indent + 1)}batch-size = {block.BatchSize.Value}");
        if (block.Exclude.Count > 0)
        {
            sb.AppendLine($"{I(indent + 1)}exclude {{");
            foreach (var e in block.Exclude)
                sb.AppendLine($"{I(indent + 2)}{QuoteString(e)}");
            sb.AppendLine($"{I(indent + 1)}}}");
        }
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatOutputBlock(StringBuilder sb, OutputBlock block, int indent)
    {
        sb.AppendLine($"{I(indent)}output {{");
        if (block.Binaries  is not null) sb.AppendLine($"{I(indent + 1)}binaries = {QuoteString(block.Binaries)}");
        if (block.Libraries is not null) sb.AppendLine($"{I(indent + 1)}libraries = {QuoteString(block.Libraries)}");
        if (block.Symbols   is not null) sb.AppendLine($"{I(indent + 1)}symbols = {QuoteString(block.Symbols)}");
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatCopyBlock(StringBuilder sb, CopyBlock block, int indent)
    {
        sb.AppendLine($"{I(indent)}copy {{");
        if (block.Items.Count > 0)
        {
            // Align "to" column for readability
            int maxFromLen = block.Items.Max(ci => QuoteString(ci.From).Length);
            foreach (var ci in block.Items)
            {
                var fromQ = QuoteString(ci.From);
                var toQ   = QuoteString(ci.To);
                var pad   = new string(' ', maxFromLen - fromQ.Length);
                sb.AppendLine($"{I(indent + 1)}from {fromQ}{pad}  to {toQ}");
            }
        }
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatBuildRuleBlock(StringBuilder sb, BuildRuleBlock block, int indent)
    {
        sb.AppendLine($"{I(indent)}rule {QuoteString(block.Name)} {{");
        sb.AppendLine($"{I(indent + 1)}command = {QuoteString(block.Command ?? string.Empty)}");
        FormatNestedStringList(sb, "inputs", block.Inputs, indent + 1);
        FormatNestedStringList(sb, "outputs", block.Outputs, indent + 1);
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatNestedStringList(
        StringBuilder sb, string name, IReadOnlyList<string> values, int indent)
    {
        sb.AppendLine($"{I(indent)}{name} {{");
        foreach (var value in values)
            sb.AppendLine($"{I(indent + 1)}{QuoteString(value)}");
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatConditionBlock(StringBuilder sb, ConditionBlock block, int indent)
    {
        sb.AppendLine($"{I(indent)}[{block.Condition}] {{");
        AppendProjectItems(sb, block.Items, indent + 1);
        sb.AppendLine($"{I(indent)}}}");
    }

    private static void FormatOptionBlock(StringBuilder sb, OptionBlock block, int indent)
    {
        sb.AppendLine($"{I(indent)}option {block.Name} {{");
        sb.AppendLine($"{I(indent + 1)}default = {(block.Default ? "true" : "false")}");
        if (block.Defines.Count > 0)
        {
            sb.AppendLine($"{I(indent + 1)}defines {{");
            foreach (var d in block.Defines)
                sb.AppendLine($"{I(indent + 2)}{QuoteString(d)}");
            sb.AppendLine($"{I(indent + 1)}}}");
        }
        if (block.Dependencies.Count > 0)
        {
            sb.AppendLine($"{I(indent + 1)}dependencies {{");
            foreach (var dep in block.Dependencies)
            {
                switch (dep.Value)
                {
                    case VersionDependency v:
                        sb.AppendLine($"{I(indent + 2)}{dep.Name} = {QuoteString(v.Version)}");
                        break;
                    case ComplexDependency c:
                        var parts = new List<string>();
                        if (c.Git     is not null) parts.Add($"git = {QuoteString(c.Git)}");
                        if (c.Tag     is not null) parts.Add($"tag = {QuoteString(c.Tag)}");
                        if (c.Commit  is not null) parts.Add($"commit = {QuoteString(c.Commit)}");
                        if (c.Path    is not null) parts.Add($"path = {QuoteString(c.Path)}");
                        if (c.Version is not null) parts.Add($"version = {QuoteString(c.Version)}");
                        if (c.Options is { Count: 1 }) parts.Add($"option = {QuoteString(c.Options[0])}");
                    else if (c.Options is { Count: > 1 }) parts.Add($"option = {{ {string.Join(" ", c.Options.Select(QuoteString))} }}");
                        sb.AppendLine($"{I(indent + 2)}{dep.Name} = {{ {string.Join(", ", parts)} }}");
                        break;
                }
            }
            sb.AppendLine($"{I(indent + 1)}}}");
        }
        sb.AppendLine($"{I(indent)}}}");
    }

    // ─── assembler block ────────────────────────────────────────────────────

    private static void FormatAssemblerBlock(StringBuilder sb, AssemblerBlock block, int indent)
    {
        sb.AppendLine($"{I(indent)}assembler {{");
        if (block.Tool != AssemblerTool.Auto)
        {
            var toolName = block.Tool switch
            {
                AssemblerTool.Nasm => "nasm",
                AssemblerTool.Yasm => "yasm",
                AssemblerTool.Gas  => "gas",
                AssemblerTool.Masm => "masm",
                _                  => "auto",
            };
            sb.AppendLine($"{I(indent + 1)}tool = {toolName}");
        }
        if (block.Format is not null)
            sb.AppendLine($"{I(indent + 1)}format = {QuoteString(block.Format)}");
        foreach (var item in block.Items)
        {
            string keyword = item.IsInclude ? "include" : "exclude";
            sb.AppendLine($"{I(indent + 1)}{keyword} {QuoteString(item.Glob)}");
        }
        if (block.Flags.Count > 0)
            FormatStringValuesBlock(sb, "flags", block.Flags, indent + 1);
        if (block.Defines.Count > 0)
            FormatStringValuesBlock(sb, "defines", block.Defines, indent + 1);
        sb.AppendLine($"{I(indent)}}}");
    }

    // ─── package block ──────────────────────────────────────────────────────

    private static void FormatPackage(StringBuilder sb, PackageDecl package)
    {
        foreach (var c in package.LeadingComments)
            sb.AppendLine($"# {c}");

        sb.AppendLine("package {");

        // If BodyItems was populated by the parser, use it to preserve order + comments
        if (package.BodyItems.Count > 0)
        {
            foreach (var item in package.BodyItems)
            {
                switch (item)
                {
                    case PackageCommentItem c:
                        sb.AppendLine($"    # {c.Text}");
                        break;
                    case PackageFieldItem f:
                        AppendPackageField(sb, package, f.FieldName);
                        break;
                }
            }
        }
        else
        {
            // Fallback for programmatically-constructed PackageDecl (no BodyItems)
            if (package.Name        is not null) sb.AppendLine($"    name = {QuoteString(package.Name)}");
            if (package.Version     is not null) sb.AppendLine($"    version = {QuoteString(package.Version)}");
            if (package.Description is not null) sb.AppendLine($"    description = {QuoteString(package.Description)}");
            if (package.License     is not null) sb.AppendLine($"    license = {QuoteString(package.License)}");
            if (package.Homepage    is not null) sb.AppendLine($"    homepage = {QuoteString(package.Homepage)}");
            if (package.Authors.Count > 0)
            {
                sb.AppendLine("    authors {");
                foreach (var a in package.Authors)
                    sb.AppendLine($"        {QuoteString(a)}");
                sb.AppendLine("    }");
            }
            if (package.Exports.Count > 0)
            {
                sb.AppendLine("    exports {");
                foreach (var (k, v) in package.Exports)
                    sb.AppendLine($"        {k} = {QuoteString(v)}");
                sb.AppendLine("    }");
            }
        }

        sb.AppendLine("}");
    }

    private static void AppendPackageField(StringBuilder sb, PackageDecl package, string fieldName)
    {
        switch (fieldName)
        {
            case "name"        when package.Name        is not null: sb.AppendLine($"    name = {QuoteString(package.Name)}"); break;
            case "version"     when package.Version     is not null: sb.AppendLine($"    version = {QuoteString(package.Version)}"); break;
            case "description" when package.Description is not null: sb.AppendLine($"    description = {QuoteString(package.Description)}"); break;
            case "license"     when package.License     is not null: sb.AppendLine($"    license = {QuoteString(package.License)}"); break;
            case "homepage"    when package.Homepage    is not null: sb.AppendLine($"    homepage = {QuoteString(package.Homepage)}"); break;
            case "authors" when package.Authors.Count > 0:
                sb.AppendLine("    authors {");
                foreach (var a in package.Authors)
                    sb.AppendLine($"        {QuoteString(a)}");
                sb.AppendLine("    }");
                break;
            case "exports" when package.Exports.Count > 0:
                sb.AppendLine("    exports {");
                foreach (var (k, v) in package.Exports)
                    sb.AppendLine($"        {k} = {QuoteString(v)}");
                sb.AppendLine("    }");
                break;
        }
    }

    // ─── Enum formatters ────────────────────────────────────────────────────

    private static string FormatProjectType(ProjectType v) => v switch
    {
        ProjectType.Executable    => "executable",
        ProjectType.Test          => "test",
        ProjectType.StaticLibrary => "static-library",
        ProjectType.SharedLibrary => "shared-library",
        ProjectType.HeaderOnly    => "header-only",
        _                         => v.ToString().ToLowerInvariant(),
    };

    private static string FormatCxxStd(CxxStd v) => v switch
    {
        CxxStd.Cxx17 => "c++17",
        CxxStd.Cxx20 => "c++20",
        CxxStd.Cxx23 => "c++23",
        _            => "c++23",
    };

    private static string FormatOptimize(OptimizeLevel v) => v switch
    {
        OptimizeLevel.None  => "none",
        OptimizeLevel.Size  => "size",
        OptimizeLevel.Speed => "speed",
        OptimizeLevel.Full  => "full",
        _                   => "none",
    };

    private static string FormatDebugInfo(DebugInfoLevel v) => v switch
    {
        DebugInfoLevel.None    => "none",
        DebugInfoLevel.Minimal => "minimal",
        DebugInfoLevel.Full    => "full",
        _                      => "none",
    };

    private static string FormatRuntimeLink(RuntimeLink v) => v switch
    {
        RuntimeLink.Static  => "static",
        RuntimeLink.Dynamic => "dynamic",
        _                   => "static",
    };

    private static string FormatLibc(LibcType v) => v switch
    {
        LibcType.Glibc => "glibc",
        LibcType.Musl  => "musl",
        _              => "glibc",
    };

    private static string FormatStdlib(StdlibType v) => v switch
    {
        StdlibType.LibCxx    => "libc++",
        StdlibType.LibStdCxx => "libstdc++",
        _                    => "libstdc++",
    };

    private static string FormatWarnings(WarningLevel v) => v switch
    {
        WarningLevel.None    => "none",
        WarningLevel.Default => "default",
        WarningLevel.All     => "all",
        WarningLevel.Extra   => "extra",
        _                    => "default",
    };
}
