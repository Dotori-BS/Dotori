namespace Dotori.Core.Parsing;

public sealed partial class Parser
{
    // ─── ParseSimpleProp ────────────────────────────────────────────────────

    private ProjectItem ParseSimpleProp(SourceLocation loc)
    {
        var key = Consume().Text;
        Expect(TokenKind.Equals);

        return key switch
        {
            "type"               => new ProjectTypeProp(ParseProjectType())                                    { Location = loc },
            "std"                => new StdProp(ParseCxxStd())                                                 { Location = loc },
            "description"        => new DescriptionProp(Expect(TokenKind.String).Text)                         { Location = loc },
            "optimize"           => new OptimizeProp(ParseOptimizeLevel())                                     { Location = loc },
            "debug-info"         => new DebugInfoProp(ParseDebugInfoLevel())                                   { Location = loc },
            "runtime-link"       => new RuntimeLinkProp(ParseRuntimeLink())                                    { Location = loc },
            "libc"               => new LibcProp(ParseLibcType())                                              { Location = loc },
            "stdlib"             => new StdlibProp(ParseStdlibType())                                          { Location = loc },
            "lto"                => new LtoProp(ParseBool())                                                   { Location = loc },
            "warnings"           => new WarningsProp(ParseWarningLevel())                                      { Location = loc },
            "warnings-as-errors" => new WarningsAsErrorsProp(ParseBool())                                      { Location = loc },
            "android-api-level"  => new AndroidApiLevelProp(int.Parse(Expect(TokenKind.Integer).Text))        { Location = loc },
            "macos-min"          => new MacosMinProp(Expect(TokenKind.String).Text)                            { Location = loc },
            "ios-min"            => new IosMinProp(Expect(TokenKind.String).Text)                              { Location = loc },
            "tvos-min"           => new TvosMinProp(Expect(TokenKind.String).Text)                             { Location = loc },
            "watchos-min"        => new WatchosMinProp(Expect(TokenKind.String).Text)                          { Location = loc },
            "c-as-cpp"           => new ForceCxxProp(ParseBool())                                              { Location = loc },
            "objc-arc"           => new ObjcArcProp(ParseBool())                                               { Location = loc },
            "objc-as-objcpp"     => new ForceObjcppProp(ParseBool())                                          { Location = loc },
            "toolchain"          => new ToolchainProp(Expect(TokenKind.String).Text)                           { Location = loc },
            _                    => throw new ParseException($"Unknown property '{key}'", loc),
        };
    }

    // ─── Enum parsers ───────────────────────────────────────────────────────

    private ProjectType ParseProjectType()
    {
        var t = Expect(TokenKind.Ident);
        return t.Text switch
        {
            "executable"     => ProjectType.Executable,
            "test"           => ProjectType.Test,
            "static-library" => ProjectType.StaticLibrary,
            "shared-library" => ProjectType.SharedLibrary,
            "header-only"    => ProjectType.HeaderOnly,
            _ => throw new ParseException($"Unknown project type '{t.Text}'", t.Location),
        };
    }

    private CxxStd ParseCxxStd()
    {
        var t = Expect(TokenKind.Ident);
        return t.Text switch
        {
            "c++17" => CxxStd.Cxx17,
            "c++20" => CxxStd.Cxx20,
            "c++23" => CxxStd.Cxx23,
            _ => throw new ParseException($"Unknown C++ standard '{t.Text}'", t.Location),
        };
    }

    private OptimizeLevel ParseOptimizeLevel()
    {
        var t = Expect(TokenKind.Ident);
        return t.Text switch
        {
            "none"  => OptimizeLevel.None,
            "size"  => OptimizeLevel.Size,
            "speed" => OptimizeLevel.Speed,
            "full"  => OptimizeLevel.Full,
            _ => throw new ParseException($"Unknown optimize level '{t.Text}'", t.Location),
        };
    }

    private DebugInfoLevel ParseDebugInfoLevel()
    {
        var t = Expect(TokenKind.Ident);
        return t.Text switch
        {
            "none"    => DebugInfoLevel.None,
            "minimal" => DebugInfoLevel.Minimal,
            "full"    => DebugInfoLevel.Full,
            _ => throw new ParseException($"Unknown debug-info level '{t.Text}'", t.Location),
        };
    }

    private RuntimeLink ParseRuntimeLink()
    {
        var t = Expect(TokenKind.Ident);
        return t.Text switch
        {
            "static"  => RuntimeLink.Static,
            "dynamic" => RuntimeLink.Dynamic,
            _ => throw new ParseException($"Unknown runtime-link '{t.Text}'", t.Location),
        };
    }

    private LibcType ParseLibcType()
    {
        var t = Expect(TokenKind.Ident);
        return t.Text switch
        {
            "glibc" => LibcType.Glibc,
            "musl"  => LibcType.Musl,
            _ => throw new ParseException($"Unknown libc type '{t.Text}'", t.Location),
        };
    }

    private StdlibType ParseStdlibType()
    {
        var t = Expect(TokenKind.Ident);
        return t.Text switch
        {
            "libc++"    => StdlibType.LibCxx,
            "libstdc++" => StdlibType.LibStdCxx,
            _ => throw new ParseException($"Unknown stdlib type '{t.Text}'", t.Location),
        };
    }

    private WarningLevel ParseWarningLevel()
    {
        var t = Expect(TokenKind.Ident);
        return t.Text switch
        {
            "none"    => WarningLevel.None,
            "default" => WarningLevel.Default,
            "all"     => WarningLevel.All,
            "extra"   => WarningLevel.Extra,
            _ => throw new ParseException($"Unknown warning level '{t.Text}'", t.Location),
        };
    }

    private bool ParseBool()
    {
        if (Current.Kind == TokenKind.BoolTrue) { Consume(); return true; }
        if (Current.Kind == TokenKind.BoolFalse) { Consume(); return false; }
        throw new ParseException($"Expected true or false, got '{Current.Text}'", Current.Location);
    }
}
