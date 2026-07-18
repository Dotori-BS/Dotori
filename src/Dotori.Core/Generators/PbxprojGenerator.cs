using System.Text;
using Dotori.Core.Model;
using Dotori.Core.Parsing;

namespace Dotori.Core.Generators;

/// <summary>
/// Generates an Xcode project (*.xcodeproj/project.pbxproj) from a flattened project model.
/// Uses the Xcode project file format (plist-like, not XML).
/// Supports macOS and iOS targets derived from the model's platform settings.
/// </summary>
public sealed class PbxprojGenerator : IBuildSystemGenerator
{
    public string FormatId => "pbxproj";

    public IReadOnlyList<(string RelativePath, string Content)> Generate(
        FlatProjectModel model,
        string config,
        string targetId)
    {
        var projectName = SanitizeName(model.Name);
        var content = GeneratePbxproj(model, config, targetId);
        return [($"{projectName}.xcodeproj/project.pbxproj", content)];
    }

    private static string GeneratePbxproj(FlatProjectModel model, string config, string targetId)
    {
        // Generate all needed UUIDs
        var projectUuid       = NewUuid();
        var mainGroupUuid     = NewUuid();
        var sourceGroupUuid   = NewUuid();
        var productGroupUuid  = NewUuid();
        var targetUuid        = NewUuid();
        var productFileUuid   = NewUuid();
        var productRefUuid    = NewUuid();
        var buildConfigListTarget  = NewUuid();
        var buildConfigListProject = NewUuid();
        var sourceBuildPhaseUuid   = NewUuid();
        var frameworksBuildPhaseUuid = NewUuid();

        var sourceFiles = ExpandSourceFiles(model);
        // (fileUuid, buildFileUuid, path)
        var sourceEntries = sourceFiles
            .Select(s => (FileUuid: NewUuid(), BuildFileUuid: NewUuid(), Path: s))
            .ToList();

        var configs = config == "both"
            ? new[] { "Debug", "Release" }
            : new[] { config == "release" ? "Release" : "Debug" };

        var targetConfigUuids  = configs.ToDictionary(c => c, _ => NewUuid());
        var projectConfigUuids = configs.ToDictionary(c => c, _ => NewUuid());

        var productName = SanitizeName(model.Name);
        var productType = model.Type switch
        {
            ProjectType.Executable    => "com.apple.product-type.tool",
            ProjectType.Test          => "com.apple.product-type.tool",
            ProjectType.StaticLibrary => "com.apple.product-type.library.static",
            ProjectType.SharedLibrary => "com.apple.product-type.library.dynamic",
            ProjectType.HeaderOnly    => "com.apple.product-type.library.static",
            _                         => "com.apple.product-type.tool",
        };
        var productExtension = model.Type switch
        {
            ProjectType.StaticLibrary => ".a",
            ProjectType.SharedLibrary => ".dylib",
            _                         => "",
        };
        var productFileName = productName + productExtension;

        var sb = new StringBuilder();
        sb.AppendLine("// !$*UTF8*$!");
        sb.AppendLine("{");
        sb.AppendLine("\tarchiveVersion = 1;");
        sb.AppendLine("\tclasses = {");
        sb.AppendLine("\t};");
        sb.AppendLine("\tobjectVersion = 56;");
        sb.AppendLine("\tobjects = {");
        sb.AppendLine();
        sb.AppendLine("/* Begin PBXBuildFile section */");

        foreach (var (_, buildFileUuid, path) in sourceEntries)
        {
            var fileUuid = sourceEntries.First(e => e.Path == path).FileUuid;
            sb.AppendLine($"\t\t{buildFileUuid} /* {Path.GetFileName(path)} in Sources */ = " +
                          $"{{isa = PBXBuildFile; fileRef = {fileUuid} /* {Path.GetFileName(path)} */; }};");
        }

        sb.AppendLine("/* End PBXBuildFile section */");
        sb.AppendLine();
        sb.AppendLine("/* Begin PBXFileReference section */");

        // Product reference
        sb.AppendLine($"\t\t{productRefUuid} /* {productFileName} */ = " +
                      $"{{isa = PBXFileReference; explicitFileType = compiled; " +
                      $"includeInIndex = 0; path = {productFileName}; sourceTree = BUILT_PRODUCTS_DIR; }};");

        foreach (var (fileUuid, _, path) in sourceEntries)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var fileType = ext switch
            {
                ".cpp" or ".cxx" or ".cc" => "sourcecode.cpp.cpp",
                ".c"                       => "sourcecode.c.c",
                ".m"                       => "sourcecode.c.objc",
                ".mm"                      => "sourcecode.cpp.objcpp",
                ".h" or ".hpp"             => "sourcecode.c.h",
                _                          => "file",
            };
            sb.AppendLine($"\t\t{fileUuid} /* {Path.GetFileName(path)} */ = " +
                          $"{{isa = PBXFileReference; fileEncoding = 4; lastKnownFileType = {fileType}; " +
                          $"path = {EscapePbxPath(path)}; sourceTree = \"<group>\"; }};");
        }

        sb.AppendLine("/* End PBXFileReference section */");
        sb.AppendLine();

        // Source build phase
        sb.AppendLine("/* Begin PBXSourcesBuildPhase section */");
        sb.AppendLine($"\t\t{sourceBuildPhaseUuid} /* Sources */ = {{");
        sb.AppendLine("\t\t\tisa = PBXSourcesBuildPhase;");
        sb.AppendLine("\t\t\tbuildActionMask = 2147483647;");
        sb.AppendLine("\t\t\tfiles = (");
        foreach (var (_, buildFileUuid, path) in sourceEntries)
            sb.AppendLine($"\t\t\t\t{buildFileUuid} /* {Path.GetFileName(path)} in Sources */,");
        sb.AppendLine("\t\t\t);");
        sb.AppendLine("\t\t\trunOnlyForDeploymentPostprocessing = 0;");
        sb.AppendLine("\t\t};");
        sb.AppendLine("/* End PBXSourcesBuildPhase section */");
        sb.AppendLine();

        // Frameworks build phase
        sb.AppendLine("/* Begin PBXFrameworksBuildPhase section */");
        sb.AppendLine($"\t\t{frameworksBuildPhaseUuid} /* Frameworks */ = {{");
        sb.AppendLine("\t\t\tisa = PBXFrameworksBuildPhase;");
        sb.AppendLine("\t\t\tbuildActionMask = 2147483647;");
        sb.AppendLine("\t\t\tfiles = (");
        sb.AppendLine("\t\t\t);");
        sb.AppendLine("\t\t\trunOnlyForDeploymentPostprocessing = 0;");
        sb.AppendLine("\t\t};");
        sb.AppendLine("/* End PBXFrameworksBuildPhase section */");
        sb.AppendLine();

        // Groups
        sb.AppendLine("/* Begin PBXGroup section */");

        // Main group
        sb.AppendLine($"\t\t{mainGroupUuid} = {{");
        sb.AppendLine("\t\t\tisa = PBXGroup;");
        sb.AppendLine("\t\t\tchildren = (");
        sb.AppendLine($"\t\t\t\t{sourceGroupUuid} /* Sources */,");
        sb.AppendLine($"\t\t\t\t{productGroupUuid} /* Products */,");
        sb.AppendLine("\t\t\t);");
        sb.AppendLine("\t\t\tsourceTree = \"<group>\";");
        sb.AppendLine("\t\t};");

        // Source group
        sb.AppendLine($"\t\t{sourceGroupUuid} /* Sources */ = {{");
        sb.AppendLine("\t\t\tisa = PBXGroup;");
        sb.AppendLine("\t\t\tchildren = (");
        foreach (var (fileUuid, _, path) in sourceEntries)
            sb.AppendLine($"\t\t\t\t{fileUuid} /* {Path.GetFileName(path)} */,");
        sb.AppendLine("\t\t\t);");
        sb.AppendLine("\t\t\tname = Sources;");
        sb.AppendLine("\t\t\tsourceTree = \"<group>\";");
        sb.AppendLine("\t\t};");

        // Product group
        sb.AppendLine($"\t\t{productGroupUuid} /* Products */ = {{");
        sb.AppendLine("\t\t\tisa = PBXGroup;");
        sb.AppendLine("\t\t\tchildren = (");
        sb.AppendLine($"\t\t\t\t{productRefUuid} /* {productFileName} */,");
        sb.AppendLine("\t\t\t);");
        sb.AppendLine("\t\t\tname = Products;");
        sb.AppendLine("\t\t\tsourceTree = \"<group>\";");
        sb.AppendLine("\t\t};");
        sb.AppendLine("/* End PBXGroup section */");
        sb.AppendLine();

        // Native target
        sb.AppendLine("/* Begin PBXNativeTarget section */");
        sb.AppendLine($"\t\t{targetUuid} /* {productName} */ = {{");
        sb.AppendLine("\t\t\tisa = PBXNativeTarget;");
        sb.AppendLine($"\t\t\tbuildConfigurationList = {buildConfigListTarget} /* Build configuration list for PBXNativeTarget \"{productName}\" */;");
        sb.AppendLine("\t\t\tbuildPhases = (");
        sb.AppendLine($"\t\t\t\t{sourceBuildPhaseUuid} /* Sources */,");
        sb.AppendLine($"\t\t\t\t{frameworksBuildPhaseUuid} /* Frameworks */,");
        sb.AppendLine("\t\t\t);");
        sb.AppendLine("\t\t\tbuildRules = (");
        sb.AppendLine("\t\t\t);");
        sb.AppendLine("\t\t\tdependencies = (");
        sb.AppendLine("\t\t\t);");
        sb.AppendLine($"\t\t\tname = {productName};");
        sb.AppendLine($"\t\t\tproductName = {productName};");
        sb.AppendLine($"\t\t\tproductReference = {productRefUuid} /* {productFileName} */;");
        sb.AppendLine($"\t\t\tproductType = \"{productType}\";");
        sb.AppendLine("\t\t};");
        sb.AppendLine("/* End PBXNativeTarget section */");
        sb.AppendLine();

        // Project
        sb.AppendLine("/* Begin PBXProject section */");
        sb.AppendLine($"\t\t{projectUuid} /* Project object */ = {{");
        sb.AppendLine("\t\t\tisa = PBXProject;");
        sb.AppendLine("\t\t\tattributes = {");
        sb.AppendLine("\t\t\t\tLastUpgradeCheck = 1500;");
        sb.AppendLine("\t\t\t\tBuildIndependentTargetsInParallel = YES;");
        sb.AppendLine("\t\t\t};");
        sb.AppendLine($"\t\t\tbuildConfigurationList = {buildConfigListProject} /* Build configuration list for PBXProject \"{productName}\" */;");
        sb.AppendLine("\t\t\tcompatibilityVersion = \"Xcode 14.0\";");
        sb.AppendLine("\t\t\tdevelopmentRegion = en;");
        sb.AppendLine("\t\t\thasScannedForEncodings = 0;");
        sb.AppendLine("\t\t\tknownRegions = (en, Base);");
        sb.AppendLine($"\t\t\tmainGroup = {mainGroupUuid};");
        sb.AppendLine($"\t\t\tproductRefGroup = {productGroupUuid} /* Products */;");
        sb.AppendLine("\t\t\tprojectDirPath = \"\";");
        sb.AppendLine("\t\t\tprojectRoot = \"\";");
        sb.AppendLine("\t\t\ttargets = (");
        sb.AppendLine($"\t\t\t\t{targetUuid} /* {productName} */,");
        sb.AppendLine("\t\t\t);");
        sb.AppendLine("\t\t};");
        sb.AppendLine("/* End PBXProject section */");
        sb.AppendLine();

        // XCBuildConfiguration
        sb.AppendLine("/* Begin XCBuildConfiguration section */");

        foreach (var cfg in configs)
        {
            var isDebug = cfg == "Debug";

            // Per-target config
            sb.AppendLine($"\t\t{targetConfigUuids[cfg]} /* {cfg} */ = {{");
            sb.AppendLine("\t\t\tisa = XCBuildConfiguration;");
            sb.AppendLine("\t\t\tbuildSettings = {");
            WriteTargetBuildSettings(sb, model, isDebug);
            sb.AppendLine("\t\t\t};");
            sb.AppendLine($"\t\t\tname = {cfg};");
            sb.AppendLine("\t\t};");

            // Per-project config
            sb.AppendLine($"\t\t{projectConfigUuids[cfg]} /* {cfg} */ = {{");
            sb.AppendLine("\t\t\tisa = XCBuildConfiguration;");
            sb.AppendLine("\t\t\tbuildSettings = {");
            WriteProjectBuildSettings(sb, model, isDebug, targetId);
            sb.AppendLine("\t\t\t};");
            sb.AppendLine($"\t\t\tname = {cfg};");
            sb.AppendLine("\t\t};");
        }

        sb.AppendLine("/* End XCBuildConfiguration section */");
        sb.AppendLine();

        // XCConfigurationList
        sb.AppendLine("/* Begin XCConfigurationList section */");

        sb.AppendLine($"\t\t{buildConfigListProject} /* Build configuration list for PBXProject \"{productName}\" */ = {{");
        sb.AppendLine("\t\t\tisa = XCConfigurationList;");
        sb.AppendLine("\t\t\tbuildConfigurations = (");
        foreach (var cfg in configs)
            sb.AppendLine($"\t\t\t\t{projectConfigUuids[cfg]} /* {cfg} */,");
        sb.AppendLine("\t\t\t);");
        sb.AppendLine($"\t\t\tdefaultConfigurationName = {configs[^1]};");
        sb.AppendLine("\t\t};");

        sb.AppendLine($"\t\t{buildConfigListTarget} /* Build configuration list for PBXNativeTarget \"{productName}\" */ = {{");
        sb.AppendLine("\t\t\tisa = XCConfigurationList;");
        sb.AppendLine("\t\t\tbuildConfigurations = (");
        foreach (var cfg in configs)
            sb.AppendLine($"\t\t\t\t{targetConfigUuids[cfg]} /* {cfg} */,");
        sb.AppendLine("\t\t\t);");
        sb.AppendLine($"\t\t\tdefaultConfigurationName = {configs[^1]};");
        sb.AppendLine("\t\t};");

        sb.AppendLine("/* End XCConfigurationList section */");
        sb.AppendLine();

        sb.AppendLine("\t};");
        sb.AppendLine($"\trootObject = {projectUuid} /* Project object */;");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void WriteTargetBuildSettings(StringBuilder sb, FlatProjectModel model, bool isDebug)
    {
        // C++ standard
        var stdValue = model.Std switch
        {
            CxxStd.Cxx17 => "c++17",
            CxxStd.Cxx20 => "c++20",
            CxxStd.Cxx23 => "c++23",
            _             => "c++23",
        };
        sb.AppendLine($"\t\t\t\tCLANG_CXX_LANGUAGE_STANDARD = \"{stdValue}\";");
        sb.AppendLine("\t\t\t\tCLANG_CXX_LIBRARY = \"libc++\";");

        // Defines
        if (model.Defines.Count > 0)
        {
            sb.AppendLine("\t\t\t\tGCC_PREPROCESSOR_DEFINITIONS = (");
            foreach (var def in model.Defines)
                sb.AppendLine($"\t\t\t\t\t{QuotePbxStr(def)},");
            sb.AppendLine($"\t\t\t\t\t{(isDebug ? "\"DEBUG=1\"" : "\"NDEBUG\"")},");
            sb.AppendLine("\t\t\t\t\t\"$(inherited)\",");
            sb.AppendLine("\t\t\t\t);");
        }
        else
        {
            sb.AppendLine($"\t\t\t\tGCC_PREPROCESSOR_DEFINITIONS = ({(isDebug ? "\"DEBUG=1\"" : "\"NDEBUG\"")}, \"$(inherited)\");");
        }

        // Include paths
        if (model.Headers.Count > 0)
        {
            sb.AppendLine("\t\t\t\tHEADER_SEARCH_PATHS = (");
            foreach (var h in model.Headers)
                sb.AppendLine($"\t\t\t\t\t{QuotePbxStr(h.Path)},");
            sb.AppendLine("\t\t\t\t\t\"$(inherited)\",");
            sb.AppendLine("\t\t\t\t);");
        }

        // Optimization
        var optLevel = (isDebug, model.Optimize) switch
        {
            (true,  _)                   => "0",
            (false, OptimizeLevel.None)  => "0",
            (false, OptimizeLevel.Size)  => "s",
            (false, OptimizeLevel.Speed) => "2",
            (false, OptimizeLevel.Full)  => "3",
            _                            => "0",
        };
        sb.AppendLine($"\t\t\t\tGCC_OPTIMIZATION_LEVEL = {optLevel};");

        // Warnings
        if (model.Warnings == WarningLevel.All || model.Warnings == WarningLevel.Extra)
        {
            sb.AppendLine("\t\t\t\tGCC_WARN_UNUSED_VARIABLE = YES;");
            sb.AppendLine("\t\t\t\tGCC_WARN_ABOUT_RETURN_TYPE = YES;");
        }
        if (model.WarningsAsErrors)
            sb.AppendLine("\t\t\t\tGCC_TREAT_WARNINGS_AS_ERRORS = YES;");

        // Compile flags (other flags)
        if (model.CompileFlags.Count > 0)
            sb.AppendLine($"\t\t\t\tOTHER_CFLAGS = {QuotePbxStr(string.Join(" ", model.CompileFlags))};");

        // Link flags
        if (model.LinkFlags.Count > 0 || model.Links.Count > 0 || model.Frameworks.Count > 0)
        {
            var linkFlags = new List<string>(model.LinkFlags);
            foreach (var lib in model.Links)
                linkFlags.Add($"-l{lib}");
            foreach (var fw in model.Frameworks)
            {
                linkFlags.Add("-framework");
                linkFlags.Add(fw);
            }
            sb.AppendLine($"\t\t\t\tOTHER_LDFLAGS = {QuotePbxStr(string.Join(" ", linkFlags))};");
        }

        // Debug info
        if (model.DebugInfo != DebugInfoLevel.None || isDebug)
            sb.AppendLine("\t\t\t\tGCC_GENERATE_DEBUGGING_SYMBOLS = YES;");

        // LTO
        if (model.Lto && !isDebug)
            sb.AppendLine("\t\t\t\tLLVM_LTO = YES;");

        // Product name
        sb.AppendLine($"\t\t\t\tPRODUCT_NAME = \"$(TARGET_NAME)\";");
    }

    private static void WriteProjectBuildSettings(StringBuilder sb, FlatProjectModel model, bool isDebug, string targetId)
    {
        // macOS deployment target
        if (model.MacosMin is not null)
            sb.AppendLine($"\t\t\t\tMACOSX_DEPLOYMENT_TARGET = {model.MacosMin};");
        else if (targetId.Contains("macos"))
            sb.AppendLine("\t\t\t\tMACOSX_DEPLOYMENT_TARGET = 12.0;");

        if (model.IosMin is not null)
            sb.AppendLine($"\t\t\t\tIPHONEOS_DEPLOYMENT_TARGET = {model.IosMin};");

        sb.AppendLine("\t\t\t\tALWAYS_SEARCH_USER_PATHS = NO;");
        sb.AppendLine("\t\t\t\tCOPY_PHASE_STRIP = NO;");
        sb.AppendLine($"\t\t\t\tDEBUG_INFORMATION_FORMAT = {(isDebug ? "dwarf" : "\"dwarf-with-dsym\"")};");
        sb.AppendLine("\t\t\t\tENABLE_STRICT_OBJC_MSGSEND = YES;");
        sb.AppendLine("\t\t\t\tGCC_C_LANGUAGE_STANDARD = gnu11;");
        sb.AppendLine($"\t\t\t\tONLY_ACTIVE_ARCH = {(isDebug ? "YES" : "NO")};");
    }

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
                if (!Directory.Exists(dir)) { result.Add(pattern); continue; }

                var search = filePattern.StartsWith("**")
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;
                var pat = filePattern.TrimStart('*').TrimStart('/');
                if (string.IsNullOrEmpty(pat)) pat = "*";

                var files = Directory.GetFiles(dir, pat, search).OrderBy(f => f);
                foreach (var f in files)
                    result.Add(Path.GetRelativePath(model.ProjectDir, f).Replace('\\', '/'));
            }
            catch
            {
                result.Add(pattern);
            }
        }
        return result;
    }

    private static string NewUuid() =>
        Guid.NewGuid().ToString("N").ToUpper()[..24];

    private static string SanitizeName(string name) => name.Replace(' ', '_');

    private static string EscapePbxPath(string path) =>
        path.Contains(' ') ? $"\"{path}\"" : path;

    private static string QuotePbxStr(string s) =>
        $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
