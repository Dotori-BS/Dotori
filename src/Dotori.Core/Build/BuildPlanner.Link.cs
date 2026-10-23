using Dotori.Core;
using Dotori.Core.Linker;
using Dotori.Core.Model;
using Dotori.Core.Parsing;
using Dotori.Core.Toolchain;

namespace Dotori.Core.Build;

public sealed partial class BuildPlanner
{
    // ─── Link job ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Plan the link job (returns null for header-only projects).
    /// Uses the appropriate linker: MsvcLinker, AppleLinker, LldLinker, or EmscriptenDriver.
    /// Static libraries use lib.exe for MSVC targets and 'ar rcs' elsewhere.
    /// </summary>
    public LinkJob? PlanLinkJob(IEnumerable<string> objFiles)
    {
        if (_model.Type == ProjectType.HeaderOnly) return null;

        var outDir = Path.Combine(_model.ProjectDir, DotoriConstants.CacheDir,
            DotoriConstants.BinSubDir, $"{_targetId}-{_config.ToLower()}");
        Directory.CreateDirectory(outDir);

        var outName = GetOutputName();
        var outFile = Path.Combine(outDir, outName);

        // Static library on non-MSVC: use ar rcs
        if (_model.Type == ProjectType.StaticLibrary && _toolchain.Kind != CompilerKind.Msvc)
        {
            var args = new List<string> { "rcs", $"\"{outFile}\"" };
            foreach (var obj in objFiles) args.Add($"\"{obj}\"");
            var archiveJob = new LinkJob
            {
                InputFiles = objFiles.ToArray(),
                OutputFile = outFile,
                Args       = args.ToArray(),
            };
            archiveJob.Fingerprint = BuildActionFingerprint.Compute(FindAr(), archiveJob.Args);
            return archiveJob;
        }

        if (_model.HasSwiftSources)
        {
            var compiler = GetSwiftCompilerPath();
            var hasCxxSources = ExpandSources().Any(path =>
                !Path.GetExtension(path).Equals(".swift", StringComparison.OrdinalIgnoreCase));
            var flags = SwiftDriver.LinkFlags(
                _model, _toolchain, outFile, compiler, hasCxxSources);
            var job = LinkJobFactory.Create(objFiles, outFile, flags);
            job.Fingerprint = BuildActionFingerprint.Compute(compiler, job.Args);
            return job;
        }

        if (_toolchain.Kind == CompilerKind.Msvc)
        {
            var flags = MsvcLinker.LinkFlags(_model, _toolchain, _config, outFile);
            return WithFingerprint(MsvcLinker.MakeLinkJob(objFiles, outFile, flags));
        }
        else if (_toolchain.Kind == CompilerKind.Emscripten)
        {
            var flags = EmscriptenDriver.LinkFlags(_model, outFile);
            return WithFingerprint(EmscriptenDriver.MakeLinkJob(objFiles, outFile, flags));
        }
        else if (AppleLinker.IsAppleTarget(_toolchain))
        {
            var flags = AppleLinker.LinkFlags(_model, _toolchain, outFile);
            return WithFingerprint(AppleLinker.MakeLinkJob(objFiles, outFile, flags));
        }
        else
        {
            // Linux, Android, WASM-bare: use LLD via clang++
            var flags = LldLinker.LinkFlags(_model, _toolchain, outFile);
            return WithFingerprint(LldLinker.MakeLinkJob(objFiles, outFile, flags));
        }
    }

    private LinkJob WithFingerprint(LinkJob job)
    {
        job.Fingerprint = BuildActionFingerprint.Compute(GetLinkerPath(), job.Args);
        return job;
    }

    /// <summary>
    /// Returns the correct linker path for this project's link step.
    /// Static libraries use the platform archiver; other projects use the toolchain linker.
    /// </summary>
    public string GetLinkerPath()
    {
        if (_model.Type == ProjectType.StaticLibrary)
        {
            if (_toolchain.Kind == CompilerKind.Msvc)
            {
                if (!string.IsNullOrWhiteSpace(_toolchain.Msvc?.LibExePath))
                    return _toolchain.Msvc.LibExePath;

                var linkerDir = Path.GetDirectoryName(_toolchain.LinkerPath);
                return string.IsNullOrEmpty(linkerDir)
                    ? "lib.exe"
                    : Path.Combine(linkerDir, "lib.exe");
            }
            return FindAr();
        }
        if (_model.HasSwiftSources) return GetSwiftCompilerPath();
        return _toolchain.LinkerPath;
    }

    private string FindAr()
    {
        // For MinGW cross-compilation, prefer the prefixed archiver (e.g. x86_64-w64-mingw32-ar).
        // Fall back to llvm-ar or plain ar.
        var candidates = _toolchain.IsMinGW
            ? new[] { $"{_toolchain.TargetTriple}-ar", "llvm-ar", "ar" }
            : new[] { "llvm-ar", "ar" };

        foreach (var candidate in candidates)
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                var full = Path.Combine(dir, candidate);
                if (File.Exists(full)) return full;
                if (OperatingSystem.IsWindows() && File.Exists(full + ".exe")) return full + ".exe";
            }
        }
        return "ar"; // fallback: assume ar is in PATH
    }

    // ─── Output name ──────────────────────────────────────────────────────────

    private string GetOutputName()
    {
        bool isWindows = _targetId.StartsWith("windows", StringComparison.OrdinalIgnoreCase)
                      || _targetId.StartsWith("uwp",     StringComparison.OrdinalIgnoreCase);
        bool isMacos   = _targetId.StartsWith("macos",   StringComparison.OrdinalIgnoreCase);
        bool isWasm    = _targetId.StartsWith("wasm",    StringComparison.OrdinalIgnoreCase);

        // MinGW static libraries use .a convention (not .lib), matching Unix ar output.
        bool isMsvcWindows = isWindows && !_toolchain.IsMinGW;

        return _model.Type switch
        {
            ProjectType.Executable     => isWindows ? $"{_model.Name}.exe"
                                        : isWasm    ? $"{_model.Name}.wasm"
                                        : _model.Name,
            ProjectType.Test           => isWindows ? $"{_model.Name}.exe"
                                        : isWasm    ? $"{_model.Name}.wasm"
                                        : _model.Name,
            ProjectType.StaticLibrary  => isMsvcWindows ? $"{_model.Name}.lib"
                                        : $"lib{_model.Name}.a",
            ProjectType.SharedLibrary  => isWindows ? $"{_model.Name}.dll"
                                        : isMacos   ? $"lib{_model.Name}.dylib"
                                        : $"lib{_model.Name}.so",
            ProjectType.HeaderOnly     => string.Empty,
            _                          => _model.Name,
        };
    }
}
