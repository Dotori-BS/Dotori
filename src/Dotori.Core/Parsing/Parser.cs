namespace Dotori.Core.Parsing;

public sealed class ParseException(string message, SourceLocation location)
    : Exception($"{location}: {message}")
{
    public SourceLocation Location { get; } = location;
}

/// <summary>
/// Recursive-descent parser for the .dotori DSL.
/// Split into partial files:
///   Parser.cs        — infrastructure, entry point, project/package top-level
///   Parser.Blocks.cs — block-level parsers (sources, headers, dependencies, …)
///   Parser.Enums.cs  — enum/primitive value parsers (ParseSimpleProp, ParseBool, …)
/// </summary>
public sealed partial class Parser
{
    private readonly List<Token> _tokens;
    private readonly string _filePath;
    private int _pos;

    public Parser(List<Token> tokens, string filePath)
    {
        _tokens = tokens;
        _filePath = filePath;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private Token Current => _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1];
    private Token Peek(int offset = 1) => (_pos + offset) < _tokens.Count ? _tokens[_pos + offset] : _tokens[^1];

    private Token Consume()
    {
        var t = Current;
        _pos++;
        return t;
    }

    private Token Expect(TokenKind kind)
    {
        if (Current.Kind != kind)
            throw new ParseException(
                $"Expected {kind} but got {Current.Kind} '{Current.Text}'",
                Current.Location);
        return Consume();
    }

    private bool TryConsume(TokenKind kind)
    {
        if (Current.Kind == kind) { Consume(); return true; }
        return false;
    }

    /// <summary>
    /// Consumes all consecutive <see cref="TokenKind.Comment"/> tokens and returns their texts.
    /// Used to collect leading comments before a declaration or project item.
    /// </summary>
    private List<string> ConsumeComments()
    {
        var comments = new List<string>();
        while (Current.Kind == TokenKind.Comment)
            comments.Add(Consume().Text);
        return comments;
    }

    /// <summary>
    /// Skips (discards) all consecutive <see cref="TokenKind.Comment"/> tokens.
    /// Used inside inner blocks (sources, defines, etc.) where comments cannot be
    /// meaningfully attached to a typed sub-item.
    /// </summary>
    private void SkipComments()
    {
        while (Current.Kind == TokenKind.Comment)
            Consume();
    }

    // ─── Entry ─────────────────────────────────────────────────────────────

    public DotoriFile ParseFile()
    {
        ProjectDecl? project = null;
        PackageDecl? package = null;

        // Collect any leading comments; they will be attached to the first declaration
        var pendingComments = ConsumeComments();

        while (Current.Kind != TokenKind.Eof)
        {
            if (Current.Kind == TokenKind.Ident && Current.Text == "project")
            {
                if (project != null)
                    throw new ParseException("Duplicate 'project' declaration", Current.Location);
                project = ParseProject(pendingComments);
                pendingComments = ConsumeComments();
            }
            else if (Current.Kind == TokenKind.Ident && Current.Text == "package")
            {
                if (package != null)
                    throw new ParseException("Duplicate 'package' declaration", Current.Location);
                package = ParsePackage(pendingComments);
                pendingComments = ConsumeComments();
            }
            else
            {
                throw new ParseException(
                    $"Expected 'project' or 'package', got '{Current.Text}'",
                    Current.Location);
            }
        }

        var file = new DotoriFile
        {
            FilePath = _filePath,
            Project  = project,
            Package  = package,
        };
        // Any comments after the last declaration become trailing file comments
        file.TrailingComments.AddRange(pendingComments);
        return file;
    }

    // ─── Project ───────────────────────────────────────────────────────────

    private ProjectDecl ParseProject(List<string> leadingComments)
    {
        var loc = Current.Location;
        Expect(TokenKind.Ident); // "project"
        var name = Expect(TokenKind.Ident).Text;
        Expect(TokenKind.LBrace);

        var decl = new ProjectDecl { Location = loc, Name = name };
        decl.LeadingComments.AddRange(leadingComments);

        while (Current.Kind != TokenKind.RBrace && Current.Kind != TokenKind.Eof)
        {
            decl.Items.Add(ParseProjectItem());
        }

        Expect(TokenKind.RBrace);
        return decl;
    }

    private ProjectItem ParseProjectItem()
    {
        // Inline comment line
        if (Current.Kind == TokenKind.Comment)
        {
            var commentLoc  = Current.Location;
            var commentText = Consume().Text;
            return new CommentItem(commentText) { Location = commentLoc };
        }

        // Condition block: [windows] { ... }
        if (Current.Kind == TokenKind.LBracket)
            return ParseConditionBlock();

        if (Current.Kind != TokenKind.Ident)
            throw new ParseException($"Expected identifier, got '{Current.Text}'", Current.Location);

        var loc = Current.Location;
        return Current.Text switch
        {
            "type"               => ParseSimpleProp(loc),
            "std"                => ParseSimpleProp(loc),
            "description"        => ParseSimpleProp(loc),
            "optimize"           => ParseSimpleProp(loc),
            "debug-info"         => ParseSimpleProp(loc),
            "runtime-link"       => ParseSimpleProp(loc),
            "libc"               => ParseSimpleProp(loc),
            "stdlib"             => ParseSimpleProp(loc),
            "lto"                => ParseSimpleProp(loc),
            "warnings"           => ParseSimpleProp(loc),
            "warnings-as-errors" => ParseSimpleProp(loc),
            "android-api-level"  => ParseSimpleProp(loc),
            "macos-min"          => ParseSimpleProp(loc),
            "ios-min"            => ParseSimpleProp(loc),
            "tvos-min"           => ParseSimpleProp(loc),
            "watchos-min"        => ParseSimpleProp(loc),
            "c-as-cpp"           => ParseSimpleProp(loc),
            "toolchain"          => ParseSimpleProp(loc),
            "emscripten-flags"   => ParseEmscriptenFlags(loc),
            "sources"            => ParseSourcesBlock(loc, isModules: false),
            "modules"            => ParseSourcesBlock(loc, isModules: true),
            "headers"            => ParseHeadersBlock(loc),
            "defines"            => ParseDefinesBlock(loc),
            "links"              => ParseLinksBlock(loc),
            "frameworks"         => ParseFrameworksBlock(loc),
            "framework-paths"    => ParseFrameworkPathsBlock(loc),
            "compile-flags"      => ParseCompileFlagsBlock(loc),
            "link-flags"         => ParseLinkFlagsBlock(loc),
            "resources"          => ParseResourcesBlock(loc),
            "manifest"           => ParseManifestProp(loc),
            "dependencies"       => ParseDependenciesBlock(loc),
            "pch"                => ParsePchBlock(loc),
            "unity-build"        => ParseUnityBuildBlock(loc),
            "output"             => ParseOutputBlock(loc),
            "pre-build"          => ParseBuildScriptBlock(loc, isPost: false),
            "post-build"         => ParseBuildScriptBlock(loc, isPost: true),
            "copy"               => ParseCopyBlock(loc),
            "option"             => ParseOptionBlock(loc),
            "assembler"          => ParseAssemblerBlock(loc),
            _                    => throw new ParseException($"Unknown project item '{Current.Text}'", loc),
        };
    }

    private ConditionBlock ParseConditionBlock()
    {
        var loc = Current.Location;
        Expect(TokenKind.LBracket);

        // condition = ident { "." ident }
        var atoms = new List<string>();
        atoms.Add(Expect(TokenKind.Ident).Text);
        while (Current.Kind == TokenKind.Dot)
        {
            Consume(); // "."
            atoms.Add(Expect(TokenKind.Ident).Text);
        }
        Expect(TokenKind.RBracket);

        Expect(TokenKind.LBrace);
        var block = new ConditionBlock(new ConditionExpr(atoms)) { Location = loc };
        // ParseProjectItem handles Comment tokens directly
        while (Current.Kind != TokenKind.RBrace && Current.Kind != TokenKind.Eof)
            block.Items.Add(ParseProjectItem());
        Expect(TokenKind.RBrace);
        return block;
    }

    // ─── Package ───────────────────────────────────────────────────────────

    private PackageDecl ParsePackage(List<string> leadingComments)
    {
        var loc = Current.Location;
        Expect(TokenKind.Ident); // "package"
        Expect(TokenKind.LBrace);

        var decl = new PackageDecl { Location = loc };
        decl.LeadingComments.AddRange(leadingComments);

        while (Current.Kind != TokenKind.RBrace && Current.Kind != TokenKind.Eof)
        {
            // Comment inside package block
            if (Current.Kind == TokenKind.Comment)
            {
                var commentLoc  = Current.Location;
                var commentText = Consume().Text;
                decl.BodyItems.Add(new PackageCommentItem(commentText) { Location = commentLoc });
                continue;
            }

            if (Current.Kind != TokenKind.Ident)
                throw new ParseException($"Expected identifier in package block", Current.Location);

            var fieldLoc = Current.Location;
            var key      = Consume().Text;
            decl.BodyItems.Add(new PackageFieldItem(key) { Location = fieldLoc });

            switch (key)
            {
                case "name":
                    Expect(TokenKind.Equals);
                    decl.Name = Expect(TokenKind.String).Text;
                    break;
                case "version":
                    Expect(TokenKind.Equals);
                    decl.Version = Expect(TokenKind.String).Text;
                    break;
                case "description":
                    Expect(TokenKind.Equals);
                    decl.Description = Expect(TokenKind.String).Text;
                    break;
                case "license":
                    Expect(TokenKind.Equals);
                    decl.License = Expect(TokenKind.String).Text;
                    break;
                case "homepage":
                    Expect(TokenKind.Equals);
                    decl.Homepage = Expect(TokenKind.String).Text;
                    break;
                case "authors":
                    Expect(TokenKind.LBrace);
                    while (Current.Kind == TokenKind.String)
                        decl.Authors.Add(Consume().Text);
                    Expect(TokenKind.RBrace);
                    break;
                case "exports":
                    Expect(TokenKind.LBrace);
                    while (Current.Kind == TokenKind.Ident)
                    {
                        var exportName = Consume().Text;
                        Expect(TokenKind.Equals);
                        decl.Exports[exportName] = Expect(TokenKind.String).Text;
                    }
                    Expect(TokenKind.RBrace);
                    break;
                default:
                    throw new ParseException($"Unknown package field '{key}'", Current.Location);
            }
        }

        Expect(TokenKind.RBrace);
        return decl;
    }
}
