using System.Text;
using Dotori.Core.Model;
using Dotori.Core.Parsing;

namespace Dotori.Core.Generators;

/// <summary>Generates a Visual Studio solution for a Dotori project dependency graph.</summary>
public static class VisualStudioSolutionGenerator
{
    private const string VcxprojTypeGuid = "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}";

    public static (string FileName, string Content) Generate(
        IReadOnlyList<FlatProjectModel> models,
        FlatProjectModel rootModel,
        string solutionDir,
        string config,
        string targetId,
        IReadOnlyDictionary<string, string>? generatedDirectories = null)
    {
        var configurations = config == "both"
            ? new[] { "Debug", "Release" }
            : new[] { config == "release" ? "Release" : "Debug" };
        var platforms = VcxprojGenerator.ResolvePlatforms(targetId);
        var byName = models.ToDictionary(model => model.Name, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Visual Studio Version 17");
        sb.AppendLine("VisualStudioVersion = 17.0.31903.59");
        sb.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");

        foreach (var model in models.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase))
        {
            var projectGuid = VcxprojGenerator.FormatGuid(VcxprojGenerator.ProjectGuid(model));
            var projectDirectory = generatedDirectories?.GetValueOrDefault(model.DotoriPath) ?? model.ProjectDir;
            var projectPath = Path.GetRelativePath(
                    solutionDir, Path.Combine(projectDirectory, VcxprojGenerator.ProjectFileName(model)))
                .Replace('/', '\\');
            sb.AppendLine($"Project(\"{VcxprojTypeGuid}\") = \"{model.Name}\", \"{projectPath}\", \"{projectGuid}\"");

            var dependencyGuids = model.Dependencies
                .Where(dep => dep.Value is ComplexDependency { Path: not null })
                .Select(dep => byName.GetValueOrDefault(dep.Name))
                .Where(dep => dep is not null)
                .Select(dep => VcxprojGenerator.FormatGuid(VcxprojGenerator.ProjectGuid(dep!)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (dependencyGuids.Count > 0)
            {
                sb.AppendLine("\tProjectSection(ProjectDependencies) = postProject");
                foreach (var dependencyGuid in dependencyGuids)
                    sb.AppendLine($"\t\t{dependencyGuid} = {dependencyGuid}");
                sb.AppendLine("\tEndProjectSection");
            }
            sb.AppendLine("EndProject");
        }

        sb.AppendLine("Global");
        sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        foreach (var configuration in configurations)
        foreach (var platform in platforms)
            sb.AppendLine($"\t\t{configuration}|{platform} = {configuration}|{platform}");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var model in models.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase))
        {
            var guid = VcxprojGenerator.FormatGuid(VcxprojGenerator.ProjectGuid(model));
            foreach (var configuration in configurations)
            foreach (var platform in platforms)
            {
                sb.AppendLine($"\t\t{guid}.{configuration}|{platform}.ActiveCfg = {configuration}|{platform}");
                sb.AppendLine($"\t\t{guid}.{configuration}|{platform}.Build.0 = {configuration}|{platform}");
            }
        }
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("EndGlobal");

        return ($"{VcxprojGenerator.SanitizeName(rootModel.Name)}.sln", sb.ToString());
    }
}
