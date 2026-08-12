using System.CommandLine;
using Dotori.Core.Generators;
using Dotori.Core.Graph;
using Dotori.Core.Model;
using Dotori.Core.Toolchain;

namespace Dotori.Cli.Commands;

internal static class ExportBuildSystemCommandFactory
{
    private static readonly Dictionary<string, IBuildSystemGenerator> Generators = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["cmake"]    = new CMakeGenerator(),
        ["meson"]    = new MesonGenerator(),
        ["vcxproj"]  = new VcxprojGenerator(),
        ["pbxproj"]  = new PbxprojGenerator(),
        ["ninja"]    = new NinjaGenerator(),
        ["makefile"] = new MakefileGenerator(),
    };

    public static Command Create()
    {
        var command = new Command(
            "build-system",
            "Export project as a build system file (cmake, meson, vcxproj, pbxproj, ninja, makefile)");

        var formatOption = new Option<string>("--format")
        {
            Description        = "Output format: cmake (default), meson, vcxproj, pbxproj, ninja, makefile",
            DefaultValueFactory = _ => "cmake",
        };
        var projectOption     = new Option<string?>("--project")      { Description = "Path to .dotori file or directory" };
        var outputOption      = new Option<string?>("--output")       { Description = "Output directory (default: project directory)" };
        var configOption      = new Option<string>("--config")
        {
            Description         = "Build configuration: debug, release, or both (default: both)",
            DefaultValueFactory = _ => "both",
        };
        var targetOption      = new Option<string?>("--target")       { Description = "Build target triple (e.g. macos-arm64, windows-x64)" };
        var compilerOption    = new Option<string?>("--compiler")     { Description = "Compiler to use (msvc, clang)" };
        var runtimeLinkOption = new Option<string?>("--runtime-link") { Description = "Runtime link mode (static, dynamic)" };
        var libcOption        = new Option<string?>("--libc")         { Description = "C runtime library (glibc, musl)" };
        var stdlibOption      = new Option<string?>("--stdlib")       { Description = "C++ standard library (libc++, libstdc++)" };

        command.Add(formatOption);
        command.Add(projectOption);
        command.Add(outputOption);
        command.Add(configOption);
        command.Add(targetOption);
        command.Add(compilerOption);
        command.Add(runtimeLinkOption);
        command.Add(libcOption);
        command.Add(stdlibOption);

        command.SetAction((parseResult, ct) =>
        {
            var format      = parseResult.GetValue(formatOption)!;
            var projectArg  = parseResult.GetValue(projectOption);
            var outputArg   = parseResult.GetValue(outputOption);
            var config      = parseResult.GetValue(configOption)!;
            var targetArg   = parseResult.GetValue(targetOption);
            var compiler    = parseResult.GetValue(compilerOption);
            var runtimeLink = parseResult.GetValue(runtimeLinkOption);
            var libc        = parseResult.GetValue(libcOption);
            var stdlib      = parseResult.GetValue(stdlibOption);

            // Validate format
            if (!Generators.TryGetValue(format, out var generator))
            {
                Console.Error.WriteLine(
                    $"error: unknown format '{format}'. " +
                    $"Available: {string.Join(", ", Generators.Keys)}");
                return Task.FromResult(1);
            }

            // Validate config
            if (config is not ("debug" or "release" or "both"))
            {
                Console.Error.WriteLine(
                    $"error: unknown config '{config}'. Expected: debug, release, or both.");
                return Task.FromResult(1);
            }

            var targetId = BuildContext.ResolveTargetId(targetArg);

            // Resolve project paths
            var paths = BuildContext.ResolveProjectPaths(projectArg, buildAll: false);
            if (paths.Count == 0) return Task.FromResult(1);

            ToolchainInfo? selectedToolchain = null;
            if (compiler is not null)
            {
                selectedToolchain = BuildContext.DetectToolchain(
                    targetId, compiler, paths.FirstOrDefault(), libc);
                if (selectedToolchain is null) return Task.FromResult(1);
            }

            var ctx = BuildContext.MakeTargetContext(targetId, config == "both" ? "debug" : config,
                runtimeLink, libc, stdlib,
                compiler: selectedToolchain?.Kind == CompilerKind.Msvc ? "msvc" :
                          selectedToolchain is not null ? "clang" : null);

            // Build DAG
            var dag = BuildContext.BuildDag(paths);
            if (dag is null) return Task.FromResult(1);

            var buildOrder = ProjectDagBuilder.BuildLevels(dag);
            var orderedNodes = buildOrder.SelectMany(level => level).ToList();
            var commonProjectDir = FindCommonDirectory(orderedNodes.Select(node => node.ProjectDir));
            var rootPath = Path.GetFullPath(paths[0]);
            string? rootOutputDir = null;
            FlatProjectModel? rootModel = null;
            var generatedModels = new List<FlatProjectModel>();
            var generatedDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var written = 0;

            foreach (var node in orderedNodes)
            {
                var model = BuildContext.FlattenProject(node.DotoriPath, ctx, node);
                if (model is null)
                {
                    Console.Error.WriteLine(
                        $"Warning: Could not flatten '{node.DotoriPath}', skipping.");
                    continue;
                }

                var files = generator.Generate(model, config, targetId);

                // Preserve the projects' relative layout when a dependency graph is
                // exported into one directory. This keeps add_subdirectory() paths valid
                // and prevents CMakeLists.txt files from overwriting one another.
                var outDir = ResolveOutputDirectory(
                    outputArg, model.ProjectDir, commonProjectDir, orderedNodes.Count > 1);

                if (Path.GetFullPath(node.DotoriPath).Equals(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    rootOutputDir = outDir;
                    rootModel = model;
                }
                generatedModels.Add(model);
                generatedDirectories[model.DotoriPath] = outDir;

                foreach (var (relativePath, content) in files)
                {
                    var fullPath = Path.Combine(outDir, relativePath);
                    var fileDir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(fileDir))
                        Directory.CreateDirectory(fileDir);

                    File.WriteAllText(fullPath, content);
                    Console.WriteLine($"Generated '{fullPath}'");
                    written++;
                }
            }

            if (format.Equals("cmake", StringComparison.OrdinalIgnoreCase) && rootOutputDir is not null)
            {
                var presetsPath = Path.Combine(rootOutputDir, "CMakePresets.json");
                Directory.CreateDirectory(rootOutputDir);
                File.WriteAllText(
                    presetsPath,
                    CMakePresetsGenerator.Generate(config, targetId, selectedToolchain));
                Console.WriteLine($"Generated '{presetsPath}'");
                written++;
            }

            if (format.Equals("vcxproj", StringComparison.OrdinalIgnoreCase) &&
                rootOutputDir is not null && rootModel is not null)
            {
                var (fileName, content) = VisualStudioSolutionGenerator.Generate(
                    generatedModels, rootModel, rootOutputDir, config, targetId, generatedDirectories);
                var solutionPath = Path.Combine(rootOutputDir, fileName);
                File.WriteAllText(solutionPath, content);
                Console.WriteLine($"Generated '{solutionPath}'");
                written++;
            }

            if (written == 0)
                Console.Error.WriteLine("warning: No files were generated.");

            return Task.FromResult(0);
        });

        return command;
    }

    private static string ResolveOutputDirectory(
        string? outputArg,
        string projectDir,
        string commonProjectDir,
        bool hasMultipleProjects)
    {
        if (outputArg is null) return projectDir;

        var outputRoot = Path.IsPathRooted(outputArg) ? outputArg : Path.GetFullPath(outputArg);
        if (!hasMultipleProjects) return outputRoot;

        var relativeDir = Path.GetRelativePath(commonProjectDir, projectDir);
        return relativeDir == "." ? outputRoot : Path.Combine(outputRoot, relativeDir);
    }

    private static string FindCommonDirectory(IEnumerable<string> directories)
    {
        var paths = directories.Select(Path.GetFullPath).ToList();
        if (paths.Count == 0) return Directory.GetCurrentDirectory();

        var common = paths[0];
        foreach (var path in paths.Skip(1))
        {
            while (!IsSameOrChildPath(path, common))
            {
                var parent = Directory.GetParent(common);
                if (parent is null) return Path.GetPathRoot(common) ?? common;
                common = parent.FullName;
            }
        }

        return common;
    }

    private static bool IsSameOrChildPath(string path, string candidateParent)
    {
        var relative = Path.GetRelativePath(candidateParent, path);
        return relative == "." ||
               (!relative.Equals("..", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
