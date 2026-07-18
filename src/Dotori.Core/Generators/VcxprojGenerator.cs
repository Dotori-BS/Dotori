using System.Text;
using System.Xml.Linq;
using Dotori.Core.Model;
using Dotori.Core.Parsing;

namespace Dotori.Core.Generators;

/// <summary>
/// Generates .vcxproj and .vcxproj.filters from a flattened project model.
/// Targets Visual Studio 2022 (v143 toolset) by default.
/// </summary>
public sealed class VcxprojGenerator : IBuildSystemGenerator
{
    public string FormatId => "vcxproj";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/developer/msbuild/2003";

    public IReadOnlyList<(string RelativePath, string Content)> Generate(
        FlatProjectModel model,
        string config,
        string targetId)
    {
        var configs = config == "both"
            ? new[] { "Debug", "Release" }
            : new[] { config == "release" ? "Release" : "Debug" };

        var platforms = ResolvePlatforms(targetId);

        var vcxprojContent  = GenerateVcxproj(model, configs, platforms);
        var filtersContent  = GenerateFilters(model);
        var projectFileName = $"{SanitizeName(model.Name)}.vcxproj";

        return
        [
            (projectFileName,            vcxprojContent),
            ($"{projectFileName}.filters", filtersContent),
        ];
    }

    // ─── .vcxproj ──────────────────────────────────────────────────────────────

    private static string GenerateVcxproj(
        FlatProjectModel model,
        string[] configs,
        string[] platforms)
    {
        var root = new XElement(Ns + "Project",
            new XAttribute("DefaultTargets", "Build"),
            new XAttribute("ToolsVersion", "17.0"),
            new XAttribute("xmlns", Ns.NamespaceName));

        // Project GUID
        var guid = Guid.NewGuid().ToString("B").ToUpper();

        // ItemGroup: ProjectConfigurations
        var projConfs = new XElement(Ns + "ItemGroup",
            new XAttribute("Label", "ProjectConfigurations"));
        foreach (var cfg in configs)
        foreach (var plat in platforms)
        {
            projConfs.Add(new XElement(Ns + "ProjectConfiguration",
                new XAttribute("Include", $"{cfg}|{plat}"),
                new XElement(Ns + "Configuration", cfg),
                new XElement(Ns + "Platform", plat)));
        }
        root.Add(projConfs);

        // PropertyGroup: Globals
        root.Add(new XElement(Ns + "PropertyGroup",
            new XAttribute("Label", "Globals"),
            new XElement(Ns + "VCProjectVersion", "17.0"),
            new XElement(Ns + "Keyword", "Win32Proj"),
            new XElement(Ns + "ProjectGuid", guid),
            new XElement(Ns + "RootNamespace", SanitizeName(model.Name)),
            new XElement(Ns + "WindowsTargetPlatformVersion", "10.0")));

        // Import default props
        root.Add(MsBuildImport("$(VCTargetsPath)\\Microsoft.Cpp.Default.props"));

        // PropertyGroup: Configuration per config+platform
        var configType = model.Type switch
        {
            ProjectType.Executable    => "Application",
            ProjectType.Test          => "Application",
            ProjectType.StaticLibrary => "StaticLibrary",
            ProjectType.SharedLibrary => "DynamicLibrary",
            ProjectType.HeaderOnly    => "StaticLibrary",
            _                         => "Application",
        };

        foreach (var cfg in configs)
        foreach (var plat in platforms)
        {
            root.Add(new XElement(Ns + "PropertyGroup",
                new XAttribute("Condition", $"'$(Configuration)|$(Platform)'=='{cfg}|{plat}'"),
                new XAttribute("Label", "Configuration"),
                new XElement(Ns + "ConfigurationType", configType),
                new XElement(Ns + "UseDebugLibraries", cfg == "Debug" ? "true" : "false"),
                new XElement(Ns + "PlatformToolset", "v143"),
                new XElement(Ns + "CharacterSet", "Unicode")));
        }

        // Import Cpp props
        root.Add(MsBuildImport("$(VCTargetsPath)\\Microsoft.Cpp.props"));

        // ItemDefinitionGroup per config
        foreach (var cfg in configs)
        foreach (var plat in platforms)
        {
            root.Add(BuildItemDefinitionGroup(model, cfg, plat));
        }

        // ItemGroup: sources and headers
        root.Add(BuildSourceItemGroup(model));
        root.Add(BuildHeaderItemGroup(model));

        if (model.Resources.Count > 0)
            root.Add(BuildResourceItemGroup(model));

        // Import targets
        root.Add(MsBuildImport("$(VCTargetsPath)\\Microsoft.Cpp.targets"));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);

        var sb = new StringBuilder();
        using var writer = new Utf8StringWriter(sb);
        doc.Save(writer);
        return sb.ToString();
    }

    private static XElement BuildItemDefinitionGroup(
        FlatProjectModel model,
        string cfg,
        string plat)
    {
        var isDebug = cfg == "Debug";
        var group = new XElement(Ns + "ItemDefinitionGroup",
            new XAttribute("Condition", $"'$(Configuration)|$(Platform)'=='{cfg}|{plat}'"));

        // ClCompile
        var clCompile = new XElement(Ns + "ClCompile");

        // Preprocessor definitions
        var defines = new List<string>(model.Defines) { "%(PreprocessorDefinitions)" };
        if (isDebug) defines.Insert(0, "_DEBUG");
        else         defines.Insert(0, "NDEBUG");
        clCompile.Add(new XElement(Ns + "PreprocessorDefinitions", string.Join(";", defines)));

        // C++ standard
        var stdValue = model.Std switch
        {
            CxxStd.Cxx17 => "stdcpp17",
            CxxStd.Cxx20 => "stdcpp20",
            CxxStd.Cxx23 => "stdcpplatest",
            _             => "stdcpplatest",
        };
        clCompile.Add(new XElement(Ns + "LanguageStandard", stdValue));

        // Optimization
        var optValue = (isDebug, model.Optimize) switch
        {
            (true,  _)                   => "Disabled",
            (false, OptimizeLevel.None)  => "Disabled",
            (false, OptimizeLevel.Size)  => "MinSpace",
            (false, OptimizeLevel.Speed) => "MaxSpeed",
            (false, OptimizeLevel.Full)  => "Full",
            _                            => "Disabled",
        };
        clCompile.Add(new XElement(Ns + "Optimization", optValue));

        // Debug info
        var debugFormat = model.DebugInfo switch
        {
            DebugInfoLevel.Full    => "ProgramDatabase",
            DebugInfoLevel.Minimal => "ProgramDatabaseOnly",
            _                      => isDebug ? "ProgramDatabase" : "None",
        };
        clCompile.Add(new XElement(Ns + "DebugInformationFormat", debugFormat));

        // Runtime library
        var rtLib = (model.RuntimeLink, isDebug) switch
        {
            (RuntimeLink.Dynamic, true)  => "MultiThreadedDebugDLL",
            (RuntimeLink.Dynamic, false) => "MultiThreadedDLL",
            (RuntimeLink.Static,  true)  => "MultiThreadedDebug",
            _                            => "MultiThreaded",
        };
        clCompile.Add(new XElement(Ns + "RuntimeLibrary", rtLib));

        // Warnings
        var warnLevel = model.Warnings switch
        {
            WarningLevel.None    => "TurnOffAllWarnings",
            WarningLevel.All     => "Level3",
            WarningLevel.Extra   => "Level4",
            _                    => "Level3",
        };
        clCompile.Add(new XElement(Ns + "WarningLevel", warnLevel));
        if (model.WarningsAsErrors)
            clCompile.Add(new XElement(Ns + "TreatWarningAsError", "true"));

        // Include directories
        if (model.Headers.Count > 0)
        {
            var inclPaths = string.Join(";", model.Headers.Select(h => h.Path)) + ";%(AdditionalIncludeDirectories)";
            clCompile.Add(new XElement(Ns + "AdditionalIncludeDirectories", inclPaths));
        }

        // Additional options (compile flags)
        if (model.CompileFlags.Count > 0)
            clCompile.Add(new XElement(Ns + "AdditionalOptions",
                string.Join(" ", model.CompileFlags) + " %(AdditionalOptions)"));

        // LTO
        if (model.Lto && !isDebug)
            clCompile.Add(new XElement(Ns + "WholeProgramOptimization", "true"));

        // PCH
        if (model.Pch?.Header is not null)
        {
            clCompile.Add(new XElement(Ns + "PrecompiledHeader", "Use"));
            clCompile.Add(new XElement(Ns + "PrecompiledHeaderFile", model.Pch.Header));
        }

        group.Add(clCompile);

        // Link
        var link = new XElement(Ns + "Link");

        // Additional dependencies (libs)
        if (model.Links.Count > 0)
            link.Add(new XElement(Ns + "AdditionalDependencies",
                string.Join(";", model.Links.Select(l => EnsureLibExtension(l))) + ";%(AdditionalDependencies)"));

        // Link flags
        if (model.LinkFlags.Count > 0)
            link.Add(new XElement(Ns + "AdditionalOptions",
                string.Join(" ", model.LinkFlags) + " %(AdditionalOptions)"));

        // Debug info generation
        if (model.DebugInfo != DebugInfoLevel.None || isDebug)
            link.Add(new XElement(Ns + "GenerateDebugInformation", "true"));

        // Output directories
        if (model.Output?.Binaries is not null)
        {
            group.Add(new XElement(Ns + "PropertyGroup",
                new XElement(Ns + "OutDir", model.Output.Binaries + "\\")));
        }
        if (model.Output?.Symbols is not null)
            link.Add(new XElement(Ns + "ProgramDatabaseFile",
                $"{model.Output.Symbols}\\$(TargetName).pdb"));

        group.Add(link);

        // Pre/Post build events
        if (model.PreBuildCommands.Count > 0)
        {
            group.Add(new XElement(Ns + "PreBuildEvent",
                new XElement(Ns + "Command", string.Join("\n", model.PreBuildCommands))));
        }
        if (model.PostBuildCommands.Count > 0)
        {
            group.Add(new XElement(Ns + "PostBuildEvent",
                new XElement(Ns + "Command", string.Join("\n", model.PostBuildCommands))));
        }

        return group;
    }

    private static XElement BuildSourceItemGroup(FlatProjectModel model)
    {
        var group = new XElement(Ns + "ItemGroup");
        foreach (var src in ExpandSourceFiles(model))
            group.Add(new XElement(Ns + "ClCompile", new XAttribute("Include", src)));
        return group;
    }

    private static XElement BuildHeaderItemGroup(FlatProjectModel model)
    {
        var group = new XElement(Ns + "ItemGroup");
        foreach (var hdr in model.Headers.Select(h => h.Path))
        {
            var abs = Path.IsPathRooted(hdr) ? hdr : Path.Combine(model.ProjectDir, hdr);
            if (Directory.Exists(abs))
            {
                foreach (var f in Directory.GetFiles(abs, "*.h", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(abs, "*.hpp", SearchOption.AllDirectories))
                    .OrderBy(x => x))
                    group.Add(new XElement(Ns + "ClInclude",
                        new XAttribute("Include", Path.GetRelativePath(model.ProjectDir, f).Replace('/', '\\'))));
            }
            else
            {
                group.Add(new XElement(Ns + "ClInclude", new XAttribute("Include", hdr.Replace('/', '\\'))));
            }
        }
        return group;
    }

    private static XElement BuildResourceItemGroup(FlatProjectModel model)
    {
        var group = new XElement(Ns + "ItemGroup");
        foreach (var rc in model.Resources)
            group.Add(new XElement(Ns + "ResourceCompile", new XAttribute("Include", rc.Replace('/', '\\'))));
        return group;
    }

    // ─── .vcxproj.filters ──────────────────────────────────────────────────────

    private static string GenerateFilters(FlatProjectModel model)
    {
        var root = new XElement(Ns + "Project",
            new XAttribute("ToolsVersion", "4.0"),
            new XAttribute("xmlns", Ns.NamespaceName));

        // Collect unique filter paths from source directories
        var sources = ExpandSourceFiles(model);
        var dirs = sources
            .Select(s => Path.GetDirectoryName(s)?.Replace('\\', '/') ?? "")
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        if (dirs.Count > 0)
        {
            var filterGroup = new XElement(Ns + "ItemGroup");
            foreach (var dir in dirs)
            {
                filterGroup.Add(new XElement(Ns + "Filter",
                    new XAttribute("Include", "Source Files\\" + dir.Replace('/', '\\')),
                    new XElement(Ns + "UniqueIdentifier", $"{{{Guid.NewGuid().ToString().ToUpper()}}}")));
            }
            root.Add(filterGroup);
        }

        // Source file → filter mapping
        var srcGroup = new XElement(Ns + "ItemGroup");
        foreach (var src in sources)
        {
            var dir = Path.GetDirectoryName(src)?.Replace('/', '\\') ?? "";
            var filterVal = string.IsNullOrEmpty(dir) ? "Source Files" : $"Source Files\\{dir}";
            srcGroup.Add(new XElement(Ns + "ClCompile",
                new XAttribute("Include", src.Replace('/', '\\')),
                new XElement(Ns + "Filter", filterVal)));
        }
        root.Add(srcGroup);

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        var sb = new StringBuilder();
        using var writer = new Utf8StringWriter(sb);
        doc.Save(writer);
        return sb.ToString();
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static List<string> ExpandSourceFiles(FlatProjectModel model)
    {
        var result = new List<string>();
        foreach (var item in model.Sources)
        {
            var pattern = item.Glob;
            var abs = Path.IsPathRooted(pattern)
                ? pattern
                : Path.Combine(model.ProjectDir, pattern);

            try
            {
                var dir = Path.GetDirectoryName(abs) ?? ".";
                var filePattern = Path.GetFileName(abs);
                if (!Directory.Exists(dir)) { result.Add(pattern.Replace('/', '\\')); continue; }

                var search = filePattern.StartsWith("**")
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;
                var pat = filePattern.TrimStart('*').TrimStart('/');
                if (string.IsNullOrEmpty(pat)) pat = "*";

                var files = Directory.GetFiles(dir, pat, search).OrderBy(f => f);
                foreach (var f in files)
                    result.Add(Path.GetRelativePath(model.ProjectDir, f).Replace('/', '\\'));
            }
            catch
            {
                result.Add(pattern.Replace('/', '\\'));
            }
        }
        return result;
    }

    private static string[] ResolvePlatforms(string targetId)
    {
        if (targetId.Contains("x86") && !targetId.Contains("x64")) return ["Win32"];
        if (targetId.Contains("arm64")) return ["ARM64"];
        return ["x64"];
    }

    private static string EnsureLibExtension(string lib)
    {
        if (lib.EndsWith(".lib", StringComparison.OrdinalIgnoreCase)) return lib;
        if (lib.Contains('.')) return lib;
        return lib + ".lib";
    }

    private static string SanitizeName(string name) => name.Replace(' ', '_');

    private static XElement MsBuildImport(string project) =>
        new XElement(Ns + "Import", new XAttribute("Project", project));

    private sealed class Utf8StringWriter(StringBuilder sb) : StringWriter(sb)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
