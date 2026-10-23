namespace Dotori.Core.Toolchain;

/// <summary>The type of compiler driver.</summary>
public enum CompilerKind { Msvc, Clang, Emscripten, Swift }

/// <summary>Information about a detected toolchain for a specific target.</summary>
public sealed class ToolchainInfo
{
    public required CompilerKind Kind          { get; init; }
    public required string       CompilerPath  { get; init; }
    public required string       TargetTriple  { get; init; }

    /// <summary>Linker executable path (may equal compiler for Clang/emcc).</summary>
    public required string LinkerPath    { get; init; }

    /// <summary>Optional: path to sysroot / SDK root.</summary>
    public string? SysRoot              { get; init; }

    /// <summary>Optional: MSVC-specific include/lib roots.</summary>
    public MsvcPaths? Msvc              { get; init; }

    /// <summary>Optional: Apple-specific SDK path.</summary>
    public string? AppleSdk             { get; init; }

    /// <summary>Optional: detected external assembler executable paths.</summary>
    public AssemblerPaths? Assembler    { get; init; }

    /// <summary>
    /// True when this is a clang-cl invocation (Kind == Msvc but compiler is clang-cl.exe).
    /// clang-cl requires explicit Windows SDK include paths via -imsvc.
    /// </summary>
    public bool IsClangCl =>
        Kind == CompilerKind.Msvc &&
        Path.GetFileNameWithoutExtension(CompilerPath)
            .Equals("clang-cl", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when targeting a MinGW ABI (x86_64-w64-mingw32 etc.).
    /// Used for cross-compilation to Windows from macOS/Linux.
    /// </summary>
    public bool IsMinGW =>
        Kind == CompilerKind.Clang &&
        TargetTriple.Contains("mingw", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the selected Clang-family path is a GCC driver.</summary>
    public bool IsGccDriver
    {
        get
        {
            var name = Path.GetFileNameWithoutExtension(CompilerPath);
            return !name.Contains("clang", StringComparison.OrdinalIgnoreCase) &&
                   (name.Equals("g++", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("gcc", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("-g++", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("-gcc", StringComparison.OrdinalIgnoreCase));
        }
    }
}

/// <summary>Detected external assembler executable paths.</summary>
public sealed class AssemblerPaths
{
    public string? NasmPath { get; init; }
    public string? YasmPath { get; init; }
    public string? GasPath  { get; init; }
    /// <summary>MASM ml64.exe or ml.exe (Windows only).</summary>
    public string? MasmPath { get; init; }
}

/// <summary>MSVC-specific compiler and linker paths.</summary>
public sealed class MsvcPaths
{
    public required string VcToolsDir   { get; init; }  // e.g. VC\Tools\MSVC\14.x.y
    public required string WinSdkDir    { get; init; }  // e.g. C:\Program Files (x86)\Windows Kits\10
    public required string WinSdkVer    { get; init; }  // e.g. 10.0.22621.0
    public required string Architecture { get; init; }  // x64 / x86 / arm64

    /// <summary>MSVC librarian executable (lib.exe), used to create static libraries.</summary>
    public string? LibExePath { get; init; }

    /// <summary>Path to rc.exe (Windows SDK Resource Compiler). Null if SDK not found.</summary>
    public string? RcPath { get; init; }

    /// <summary>Path to mt.exe (Windows SDK Manifest Tool). Null if SDK not found.</summary>
    public string? MtPath { get; init; }
}
