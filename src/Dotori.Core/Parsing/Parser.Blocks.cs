namespace Dotori.Core.Parsing;

public sealed partial class Parser
{
    // ─── sources / modules block ────────────────────────────────────────────

    private SourcesBlock ParseSourcesBlock(SourceLocation loc, bool isModules)
    {
        Consume(); // "sources" or "modules"
        Expect(TokenKind.LBrace);
        var block = new SourcesBlock(isModules) { Location = loc };
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.Ident) break;
            if (Current.Text == "include" || Current.Text == "exclude")
            {
                var isInclude = Consume().Text == "include";
                var glob = Expect(TokenKind.String).Text;
                block.Items.Add(new SourceItem(isInclude, glob));
            }
            else if (isModules && Current.Text == "export-map")
            {
                Consume(); // "export-map"
                Expect(TokenKind.Equals);
                block.ExportMap = ParseBool();
            }
            else
            {
                break;
            }
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        return block;
    }

    // ─── headers block ──────────────────────────────────────────────────────

    private HeadersBlock ParseHeadersBlock(SourceLocation loc)
    {
        Consume(); // "headers"
        Expect(TokenKind.LBrace);
        var block = new HeadersBlock { Location = loc };
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.Ident ||
                (Current.Text != "public" && Current.Text != "private"))
                break;
            var isPublic = Consume().Text == "public";
            var path = Expect(TokenKind.String).Text;
            block.Items.Add(new HeaderItem(isPublic, path));
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        return block;
    }

    // ─── string-list blocks ─────────────────────────────────────────────────

    private List<string> ParseStringList()
    {
        Expect(TokenKind.LBrace);
        var list = new List<string>();
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.String && Current.Kind != TokenKind.Ident) break;
            list.Add(Consume().Text);
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        return list;
    }

    /// <summary>
    /// Consumes the current keyword token then populates <paramref name="block"/>.Values
    /// from a <c>{ string* }</c> list.  Avoids repeating the same 3-line body for
    /// DefinesBlock, LinksBlock, FrameworksBlock, CompileFlagsBlock, LinkFlagsBlock.
    /// </summary>
    private T ParseStringValuesBlock<T>(T block)
        where T : ProjectItem, IStringValuesBlock
    {
        Consume(); // keyword token
        block.Values.AddRange(ParseStringList());
        return block;
    }

    private DefinesBlock      ParseDefinesBlock     (SourceLocation loc) => ParseStringValuesBlock(new DefinesBlock      { Location = loc });
    private LinksBlock        ParseLinksBlock       (SourceLocation loc) => ParseStringValuesBlock(new LinksBlock        { Location = loc });
    private FrameworksBlock   ParseFrameworksBlock  (SourceLocation loc) => ParseStringValuesBlock(new FrameworksBlock   { Location = loc });
    private CompileFlagsBlock ParseCompileFlagsBlock(SourceLocation loc) => ParseStringValuesBlock(new CompileFlagsBlock { Location = loc });
    private LinkFlagsBlock    ParseLinkFlagsBlock   (SourceLocation loc) => ParseStringValuesBlock(new LinkFlagsBlock    { Location = loc });

    private FrameworkPathsBlock ParseFrameworkPathsBlock(SourceLocation loc)
    {
        Consume(); // "framework-paths"
        var block = new FrameworkPathsBlock { Location = loc };
        block.Paths.AddRange(ParseStringList());
        return block;
    }

    private ResourcesBlock ParseResourcesBlock(SourceLocation loc)
    {
        Consume(); // "resources"
        var block = new ResourcesBlock { Location = loc };
        block.Paths.AddRange(ParseStringList());
        return block;
    }

    private EmscriptenFlagsProp ParseEmscriptenFlags(SourceLocation loc)
    {
        Consume(); // "emscripten-flags"
        Expect(TokenKind.LBrace);
        var flags = new List<string>();
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.String) break;
            flags.Add(Consume().Text);
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        return new EmscriptenFlagsProp(flags) { Location = loc };
    }

    // ─── manifest ───────────────────────────────────────────────────────────

    private ManifestProp ParseManifestProp(SourceLocation loc)
    {
        Consume(); // "manifest"
        Expect(TokenKind.Equals);
        var val = Expect(TokenKind.String).Text;
        return new ManifestProp(val) { Location = loc };
    }

    // ─── dependencies block ─────────────────────────────────────────────────

    private DependenciesBlock ParseDependenciesBlock(SourceLocation loc)
    {
        Consume(); // "dependencies"
        Expect(TokenKind.LBrace);
        var block = new DependenciesBlock { Location = loc };
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.Ident) break;
            var ownerOrName = Consume().Text;
            string name;
            if (Current.Kind == TokenKind.Slash)
            {
                Consume(); // consume '/'
                var pkg = Expect(TokenKind.Ident).Text;
                name = $"{ownerOrName}/{pkg}";
            }
            else
            {
                name = ownerOrName;
            }
            Expect(TokenKind.Equals);
            var value = ParseDepValue();
            block.Items.Add(new DependencyItem(name, value));
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        return block;
    }

    private DependencyValue ParseDepValue()
    {
        if (Current.Kind == TokenKind.String)
            return new VersionDependency(Consume().Text);

        Expect(TokenKind.LBrace);
        var dep = new ComplexDependency();
        while (Current.Kind != TokenKind.RBrace && Current.Kind != TokenKind.Eof)
        {
            var key = Expect(TokenKind.Ident).Text;
            Expect(TokenKind.Equals);
            switch (key)
            {
                case "git":     dep.Git     = Expect(TokenKind.String).Text; break;
                case "tag":     dep.Tag     = Expect(TokenKind.String).Text; break;
                case "commit":  dep.Commit  = Expect(TokenKind.String).Text; break;
                case "path":    dep.Path    = Expect(TokenKind.String).Text; break;
                case "version": dep.Version = Expect(TokenKind.String).Text; break;
                case "option":
                    if (Current.Kind == TokenKind.String)
                    {
                        dep.Options = [Consume().Text];
                    }
                    else
                    {
                        Expect(TokenKind.LBrace);
                        dep.Options = new List<string>();
                        while (Current.Kind == TokenKind.String)
                            dep.Options.Add(Consume().Text);
                        Expect(TokenKind.RBrace);
                    }
                    break;
                default: throw new ParseException($"Unknown dependency option '{key}'", Current.Location);
            }
            TryConsume(TokenKind.Comma);
        }
        Expect(TokenKind.RBrace);
        return dep;
    }

    // ─── pch block ──────────────────────────────────────────────────────────

    private PchBlock ParsePchBlock(SourceLocation loc)
    {
        Consume(); // "pch"
        Expect(TokenKind.LBrace);
        var block = new PchBlock { Location = loc };
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.Ident) break;
            var key = Consume().Text;
            Expect(TokenKind.Equals);
            switch (key)
            {
                case "header":  block.Header  = Expect(TokenKind.String).Text; break;
                case "source":  block.Source  = Expect(TokenKind.String).Text; break;
                case "modules": block.Modules = ParseBool(); break;
                default: throw new ParseException($"Unknown pch option '{key}'", Current.Location);
            }
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        return block;
    }

    // ─── unity-build block ──────────────────────────────────────────────────

    private UnityBuildBlock ParseUnityBuildBlock(SourceLocation loc)
    {
        Consume(); // "unity-build"
        Expect(TokenKind.LBrace);
        var block = new UnityBuildBlock { Location = loc };
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.Ident) break;
            var key = Consume().Text;
            switch (key)
            {
                case "enabled":
                    Expect(TokenKind.Equals);
                    block.Enabled = ParseBool();
                    break;
                case "batch-size":
                    Expect(TokenKind.Equals);
                    block.BatchSize = int.Parse(Expect(TokenKind.Integer).Text);
                    break;
                case "exclude":
                    Expect(TokenKind.LBrace);
                    while (Current.Kind == TokenKind.String)
                        block.Exclude.Add(Consume().Text);
                    Expect(TokenKind.RBrace);
                    break;
                default: throw new ParseException($"Unknown unity-build option '{key}'", Current.Location);
            }
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        return block;
    }

    // ─── output block ───────────────────────────────────────────────────────

    private OutputBlock ParseOutputBlock(SourceLocation loc)
    {
        Consume(); // "output"
        Expect(TokenKind.LBrace);
        var block = new OutputBlock { Location = loc };
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.Ident) break;
            var key = Consume().Text;
            Expect(TokenKind.Equals);
            switch (key)
            {
                case "binaries":  block.Binaries  = Expect(TokenKind.String).Text; break;
                case "libraries": block.Libraries = Expect(TokenKind.String).Text; break;
                case "symbols":   block.Symbols   = Expect(TokenKind.String).Text; break;
                default: throw new ParseException($"Unknown output option '{key}'", Current.Location);
            }
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        return block;
    }

    // ─── option block ───────────────────────────────────────────────────────

    private OptionBlock ParseOptionBlock(SourceLocation loc)
    {
        Consume(); // "option"
        var name = Expect(TokenKind.Ident).Text;
        Expect(TokenKind.LBrace);
        bool? defaultVal = null;
        var defines = new List<string>();
        var dependencies = new List<DependencyItem>();
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.Ident) break;
            var key = Current.Text;
            switch (key)
            {
                case "default":
                    Consume();
                    Expect(TokenKind.Equals);
                    defaultVal = ParseBool();
                    break;
                case "defines":
                    Consume(); // "defines"
                    defines.AddRange(ParseStringList());
                    break;
                case "dependencies":
                    var depsBlock = ParseDependenciesBlock(Current.Location);
                    dependencies.AddRange(depsBlock.Items);
                    break;
                default:
                    throw new ParseException($"Unknown option property '{key}'", Current.Location);
            }
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        if (defaultVal is null)
            throw new ParseException($"Option '{name}' is missing required 'default' property", loc);
        var block = new OptionBlock(name, defaultVal.Value) { Location = loc };
        block.Defines.AddRange(defines);
        block.Dependencies.AddRange(dependencies);
        return block;
    }

    // ─── copy block ─────────────────────────────────────────────────────────

    private CopyBlock ParseCopyBlock(SourceLocation loc)
    {
        Consume(); // "copy"
        Expect(TokenKind.LBrace);
        var block = new CopyBlock { Location = loc };
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.Ident || Current.Text != "from") break;
            Consume(); // "from"
            var from = Expect(TokenKind.String).Text;
            if (Current.Kind != TokenKind.Ident || Current.Text != "to")
                throw new ParseException("Expected 'to' after 'from' value", Current.Location);
            Consume(); // "to"
            var to = Expect(TokenKind.String).Text;
            block.Items.Add(new CopyItem(from, to));
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        return block;
    }

    // ─── assembler block ────────────────────────────────────────────────────

    private AssemblerBlock ParseAssemblerBlock(SourceLocation loc)
    {
        Consume(); // "assembler"
        Expect(TokenKind.LBrace);
        var block = new AssemblerBlock { Location = loc };
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.Ident) break;
            var key = Current.Text;
            switch (key)
            {
                case "tool":
                    Consume();
                    Expect(TokenKind.Equals);
                    var toolText = Consume().Text;
                    block.Tool = toolText switch
                    {
                        "nasm" => AssemblerTool.Nasm,
                        "yasm" => AssemblerTool.Yasm,
                        "gas"  => AssemblerTool.Gas,
                        "as"   => AssemblerTool.Gas,
                        "masm" => AssemblerTool.Masm,
                        "auto" => AssemblerTool.Auto,
                        _      => throw new ParseException($"Unknown assembler tool '{toolText}'", Current.Location),
                    };
                    break;
                case "format":
                    Consume();
                    Expect(TokenKind.Equals);
                    block.Format = Expect(TokenKind.String).Text;
                    break;
                case "include":
                case "exclude":
                {
                    var isInclude = Consume().Text == "include";
                    var glob = Expect(TokenKind.String).Text;
                    block.Items.Add(new SourceItem(isInclude, glob));
                    break;
                }
                case "flags":
                    Consume();
                    block.Flags.AddRange(ParseStringList());
                    break;
                case "defines":
                    Consume();
                    block.Defines.AddRange(ParseStringList());
                    break;
                default:
                    throw new ParseException($"Unknown assembler option '{key}'", Current.Location);
            }
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        return block;
    }

    // ─── pre-build / post-build blocks ──────────────────────────────────────

    private ProjectItem ParseBuildScriptBlock(SourceLocation loc, bool isPost)
    {
        Consume(); // "pre-build" or "post-build"
        Expect(TokenKind.LBrace);
        if (isPost)
        {
            var block = new PostBuildBlock { Location = loc };
            while (true)
            {
                SkipComments();
                if (Current.Kind != TokenKind.String) break;
                block.Commands.Add(Consume().Text);
            }
            SkipComments();
            Expect(TokenKind.RBrace);
            return block;
        }
        else
        {
            var block = new PreBuildBlock { Location = loc };
            while (true)
            {
                SkipComments();
                if (Current.Kind != TokenKind.String) break;
                block.Commands.Add(Consume().Text);
            }
            SkipComments();
            Expect(TokenKind.RBrace);
            return block;
        }
    }

    private BuildRuleBlock ParseBuildRuleBlock(SourceLocation loc)
    {
        Consume(); // "rule"
        var name = Expect(TokenKind.String).Text;
        Expect(TokenKind.LBrace);
        var block = new BuildRuleBlock(name) { Location = loc };
        while (true)
        {
            SkipComments();
            if (Current.Kind != TokenKind.Ident) break;
            var key = Consume().Text;
            switch (key)
            {
                case "command":
                    Expect(TokenKind.Equals);
                    block.Command = Expect(TokenKind.String).Text;
                    break;
                case "inputs":
                    block.Inputs.AddRange(ParseStringList());
                    break;
                case "outputs":
                    block.Outputs.AddRange(ParseStringList());
                    break;
                default:
                    throw new ParseException($"Unknown rule property '{key}'", Current.Location);
            }
        }
        SkipComments();
        Expect(TokenKind.RBrace);
        if (string.IsNullOrWhiteSpace(block.Command))
            throw new ParseException($"Rule '{name}' is missing required 'command'", loc);
        if (block.Outputs.Count == 0)
            throw new ParseException($"Rule '{name}' must declare at least one output", loc);
        return block;
    }
}
