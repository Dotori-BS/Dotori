using Dotori.Core.Model;

namespace Dotori.Core.Build;

/// <summary>Executes declared custom build rules with input/output based invalidation.</summary>
public static class BuildRuleRunner
{
    public static async Task<int> RunAsync(
        IReadOnlyList<BuildRuleConfig> rules,
        string projectDir,
        string targetId,
        string config,
        string outputDir,
        IncrementalChecker checker,
        bool dryRun = false,
        bool explain = false,
        CancellationToken ct = default)
    {
        foreach (var rule in rules)
        {
            var unmatchedGlob = rule.Inputs.FirstOrDefault(pattern =>
                GlobExpander.IsGlobPattern(pattern) &&
                !GlobExpander.MatchGlob(projectDir, pattern).Any());
            if (unmatchedGlob is not null)
            {
                Console.Error.WriteLine($"Rule '{rule.Name}' input pattern matched no files: {unmatchedGlob}");
                return 1;
            }
            var inputs = ExpandInputs(rule.Inputs, projectDir);
            var outputs = rule.Outputs.Select(path => Absolute(path, projectDir)).ToArray();
            var missingInput = inputs.FirstOrDefault(path => !File.Exists(path));
            if (missingInput is not null)
            {
                Console.Error.WriteLine($"Rule '{rule.Name}' input does not exist: {missingInput}");
                return 1;
            }

            var fingerprint = BuildActionFingerprint.Compute(
                "custom-rule", [rule.Name, rule.Command], inputs);
            var reasons = new List<string>();
            if (outputs.Any(path => !File.Exists(path))) reasons.Add("an output is missing");
            if (inputs.Any(checker.IsChanged)) reasons.Add("an input changed");
            if (checker.IsFingerprintChanged(outputs[0], fingerprint))
                reasons.Add("the command or input set changed");

            if (reasons.Count == 0)
            {
                if (explain) Console.WriteLine($"  [explain] rule {rule.Name}: up to date");
                continue;
            }

            if (explain)
                Console.WriteLine($"  [explain] rule {rule.Name}: {string.Join("; ", reasons)}");
            if (dryRun)
            {
                Console.WriteLine($"  [dry-run] {rule.Command}");
                continue;
            }

            foreach (var output in outputs)
            {
                var directory = Path.GetDirectoryName(output);
                if (directory is not null) Directory.CreateDirectory(directory);
            }
            var exitCode = await ScriptRunner.RunAsync(
                [rule.Command], projectDir, targetId, config, outputDir, ct);
            if (exitCode != 0) return exitCode;

            var missingOutput = outputs.FirstOrDefault(path => !File.Exists(path));
            if (missingOutput is not null)
            {
                Console.Error.WriteLine($"Rule '{rule.Name}' did not create declared output: {missingOutput}");
                return 1;
            }
            foreach (var input in inputs) checker.Record(input);
            checker.RecordFingerprint(outputs[0], fingerprint);
        }
        return 0;
    }

    private static IReadOnlyList<string> ExpandInputs(
        IReadOnlyList<string> patterns, string projectDir) =>
        patterns.SelectMany(pattern => GlobExpander.IsGlobPattern(pattern)
                ? GlobExpander.MatchGlob(projectDir, pattern)
                : [Absolute(pattern, projectDir)])
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string Absolute(string path, string projectDir) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, projectDir);
}
