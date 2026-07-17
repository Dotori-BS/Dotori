using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dotori.Core.Toolchain;

public sealed class ToolchainProfileDocument
{
    public required string Kind { get; init; }
    public required string Compiler { get; init; }
    public required string Linker { get; init; }
    public required string TargetTriple { get; init; }
    public string? Sysroot { get; init; }
    public string? AppleSdk { get; init; }
    public ToolchainProfileAssembler? Assembler { get; init; }
}

public sealed class ToolchainProfileAssembler
{
    public string? Nasm { get; init; }
    public string? Yasm { get; init; }
    public string? Gas { get; init; }
    public string? Masm { get; init; }
}

[JsonSerializable(typeof(ToolchainProfileDocument))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ToolchainProfileJsonContext : JsonSerializerContext { }

/// <summary>Loads a reproducible compiler/linker configuration from JSON.</summary>
public static class ToolchainProfile
{
    public static bool IsProfilePath(string value) =>
        value.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public static ToolchainInfo Load(string path, string? baseDirectory = null)
    {
        var fullPath = ResolveProfilePath(path, baseDirectory);
        var json = File.ReadAllText(fullPath);
        var profile = JsonSerializer.Deserialize(
            json, ToolchainProfileJsonContext.Default.ToolchainProfileDocument)
            ?? throw new InvalidDataException($"Toolchain profile is empty: {fullPath}");
        var profileDir = Path.GetDirectoryName(fullPath)!;

        if (!Enum.TryParse<CompilerKind>(profile.Kind, ignoreCase: true, out var kind))
            throw new InvalidDataException(
                $"Unknown toolchain kind '{profile.Kind}'. Expected msvc, clang, or emscripten.");
        if (kind == CompilerKind.Msvc)
            throw new InvalidDataException(
                "MSVC profiles require SDK metadata and are not supported; use an msvc-* detector name.");

        return new ToolchainInfo
        {
            Kind = kind,
            CompilerPath = ResolveTool(profile.Compiler, profileDir),
            LinkerPath = ResolveTool(profile.Linker, profileDir),
            TargetTriple = Require(profile.TargetTriple, "targetTriple"),
            SysRoot = ResolveOptionalPath(profile.Sysroot, profileDir),
            AppleSdk = ResolveOptionalPath(profile.AppleSdk, profileDir),
            Assembler = profile.Assembler is null ? null : new AssemblerPaths
            {
                NasmPath = ResolveOptionalTool(profile.Assembler.Nasm, profileDir),
                YasmPath = ResolveOptionalTool(profile.Assembler.Yasm, profileDir),
                GasPath = ResolveOptionalTool(profile.Assembler.Gas, profileDir),
                MasmPath = ResolveOptionalTool(profile.Assembler.Masm, profileDir),
            },
        };
    }

    private static string ResolveProfilePath(string path, string? baseDirectory)
    {
        var resolved = Path.IsPathRooted(path)
            ? path
            : Path.Combine(baseDirectory ?? Environment.CurrentDirectory, path);
        resolved = Path.GetFullPath(resolved);
        if (!File.Exists(resolved))
            throw new FileNotFoundException("Toolchain profile was not found.", resolved);
        return resolved;
    }

    private static string ResolveTool(string value, string profileDir)
    {
        value = Require(value, "tool path");
        return HasDirectory(value) ? Path.GetFullPath(value, profileDir) : value;
    }

    private static string? ResolveOptionalTool(string? value, string profileDir) =>
        string.IsNullOrWhiteSpace(value) ? null : ResolveTool(value, profileDir);

    private static string? ResolveOptionalPath(string? value, string profileDir) =>
        string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value, profileDir);

    private static bool HasDirectory(string value) =>
        Path.IsPathRooted(value) || value.Contains('/') || value.Contains('\\');

    private static string Require(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Toolchain profile field '{field}' is required.");
}
