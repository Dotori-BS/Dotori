using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Core.Build;

public sealed partial class BuildPlanner
{
    // ─── Windows RC / Manifest ────────────────────────────────────────────────

    /// <summary>
    /// Plan compile jobs for Windows resource files (.rc → .res via rc.exe).
    /// Returns an empty list if not on MSVC or if no resources are declared.
    /// The returned jobs should be executed with rc.exe
    /// (<see cref="MsvcPaths.RcPath"/>) as the tool path.
    /// </summary>
    public IReadOnlyList<CompileJob> PlanRcJobs()
    {
        if (_model.Resources.Count == 0)           return [];
        if (_toolchain.Kind != CompilerKind.Msvc)  return [];
        if (_toolchain.Msvc?.RcPath is null)       return [];

        var jobs = new List<CompileJob>();

        foreach (var rcPath in _model.Resources)
        {
            var absRc = PathUtils.MakeAbsolute(_model.ProjectDir, rcPath);

            if (!File.Exists(absRc))
            {
                Console.Error.WriteLine($"Warning: resource file not found, skipping: {absRc}");
                continue;
            }

            var resFile = BuildOutputPath.ForSource(absRc, _cacheDir, ".res");

            var args = new List<string> { "/nologo" };

            // Pass project include paths so .rc files can use #include
            foreach (var h in _model.Headers)
                args.Add($"/I\"{PathUtils.MakeAbsolute(_model.ProjectDir, h.Path)}\"");

            // Pass defines (rc.exe supports /D like cl.exe)
            foreach (var d in _model.Defines)
                args.Add($"/D{d}");

            args.Add($"/fo\"{resFile}\"");
            args.Add($"\"{absRc}\"");

            jobs.Add(new CompileJob
            {
                SourceFile = absRc,
                OutputFile = resFile,
                Args       = args.ToArray(),
            });
        }

        return jobs;
    }

    /// <summary>
    /// Embed a .manifest file into the linked binary using mt.exe.
    /// No-op if no manifest is declared, toolchain is not MSVC, or mt.exe is not found.
    /// </summary>
    /// <param name="outputFile">Absolute path to the linked binary (.exe or .dll).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True on success or when embedding is not applicable.</returns>
    public async Task<bool> EmbedManifestAsync(string outputFile, CancellationToken ct = default)
    {
        if (_model.Manifest is null)                      return true;
        if (_toolchain.Kind != CompilerKind.Msvc)         return true;
        if (_model.Type    == ProjectType.StaticLibrary)  return true;

        if (_toolchain.Msvc?.MtPath is not { } mtPath)
        {
            Console.Error.WriteLine("Warning: mt.exe not found, skipping manifest embedding.");
            return true;
        }

        var absManifest = PathUtils.MakeAbsolute(_model.ProjectDir, _model.Manifest);

        if (!File.Exists(absManifest))
        {
            Console.Error.WriteLine($"Warning: manifest file not found, skipping: {absManifest}");
            return true;
        }

        // Resource ID: 1 = executable, 2 = DLL
        var resourceId = _model.Type == ProjectType.SharedLibrary ? "2" : "1";
        var args = $"-nologo -manifest \"{absManifest}\" -outputresource:\"{outputFile};{resourceId}\"";

        var psi = new System.Diagnostics.ProcessStartInfo(mtPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(stderr.TrimEnd());

        return proc.ExitCode == 0;
    }
}
