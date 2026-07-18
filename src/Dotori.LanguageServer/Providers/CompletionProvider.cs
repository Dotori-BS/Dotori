using Dotori.LanguageServer.Protocol;

namespace Dotori.LanguageServer.Providers;

/// <summary>
/// Provides context-aware keyword completions for .dotori files.
/// Uses simple line/text analysis to determine cursor context.
/// </summary>
public static class CompletionProvider
{
    // Completion item kinds (LSP spec)
    private const int KindKeyword = 14;
    private const int KindValue   = 12;
    private const int KindText    = 1;

    private static readonly string[] TopLevelKeywords = ["project", "package"];

    private static readonly string[] ProjectKeywords =
    [
        "type", "std", "description", "optimize", "debug-info", "runtime-link",
        "libc", "stdlib", "lto", "warnings", "warnings-as-errors",
        "android-api-level", "macos-min", "ios-min", "tvos-min", "watchos-min",
        "sources", "modules", "headers", "defines", "links", "frameworks",
        "compile-flags", "link-flags", "dependencies", "pch", "unity-build",
        "output", "pre-build", "post-build", "copy", "emscripten-flags",
        "framework-paths", "resources", "manifest",
        "option",
    ];

    private static readonly string[] PackageKeywords =
        ["name", "version", "description", "license", "homepage", "authors", "exports"];

    private static readonly string[] ConditionAtoms =
    [
        "windows", "linux", "macos", "ios", "tvos", "watchos", "android", "uwp", "wasm",
        "debug", "release",
        "msvc", "clang",
        "static", "dynamic",
        "glibc", "musl",
        "libcxx", "libstdcxx",
        "emscripten", "bare",
    ];

    // Enum values per property
    private static readonly Dictionary<string, string[]> EnumValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["type"]         = ["executable", "test", "static-library", "shared-library", "header-only"],
        ["std"]          = ["c++17", "c++20", "c++23"],
        ["optimize"]     = ["none", "size", "speed", "full"],
        ["debug-info"]   = ["none", "minimal", "full"],
        ["runtime-link"] = ["static", "dynamic"],
        ["libc"]         = ["glibc", "musl"],
        ["stdlib"]       = ["libc++", "libstdc++"],
        ["warnings"]     = ["none", "default", "all", "extra"],
        ["lto"]          = ["true", "false"],
        ["warnings-as-errors"] = ["true", "false"],
    };

    /// <summary>
    /// Compute completion items at the given position in the document.
    /// </summary>
    public static CompletionList GetCompletions(string text, int line, int character)
    {
        var lines = text.Split('\n');
        var ctx = AnalyzeContext(lines, line, character);

        var items = ctx switch
        {
            CompletionContext.TopLevel         => MakeItems(TopLevelKeywords, KindKeyword),
            CompletionContext.ProjectBlock     => MakeItems(ProjectKeywords, KindKeyword),
            CompletionContext.PackageBlock     => MakeItems(PackageKeywords, KindKeyword),
            CompletionContext.ConditionAtom    => MakeItems(ConditionAtoms, KindKeyword),
            CompletionContext.EnumValue { Property: var prop }
                when EnumValues.TryGetValue(prop, out var vals) => MakeItems(vals, KindValue),
            _                                 => new List<LspCompletionItem>(),
        };

        return new CompletionList { IsIncomplete = false, Items = items };
    }

    private enum CompletionContextKind
    {
        Unknown,
        TopLevel,
        ProjectBlock,
        PackageBlock,
        ConditionAtom,
        EnumValue,
    }

    private abstract record CompletionContext
    {
        public record TopLevel : CompletionContext;
        public record ProjectBlock : CompletionContext;
        public record PackageBlock : CompletionContext;
        public record ConditionAtom : CompletionContext;
        public record EnumValue(string Property) : CompletionContext;
        public record Unknown : CompletionContext;
    }

    private static CompletionContext AnalyzeContext(string[] lines, int line, int character)
    {
        // Look at the current line up to cursor
        var currentLine = line < lines.Length ? lines[line] : "";
        var lineUpToCursor = character <= currentLine.Length
            ? currentLine[..character]
            : currentLine;

        var trimmedLine = lineUpToCursor.TrimStart();

        // After `[` → condition atom
        if (trimmedLine.StartsWith('['))
            return new CompletionContext.ConditionAtom();

        // Determine block context by scanning backwards
        var (blockContext, lastPropName) = DetermineBlockContext(lines, line);

        // After `propname =` → enum value
        if (lastPropName is not null)
        {
            var afterEq = GetWordAfterEquals(lineUpToCursor);
            if (afterEq is not null)
                return new CompletionContext.EnumValue(lastPropName);
        }

        // Check if current line has `propname =` pattern
        var eqIdx = lineUpToCursor.IndexOf('=');
        if (eqIdx >= 0)
        {
            var beforeEq = lineUpToCursor[..eqIdx].Trim();
            // Only simple property names (no braces before =)
            if (!beforeEq.Contains('{') && !beforeEq.Contains('}'))
                return new CompletionContext.EnumValue(beforeEq);
        }

        return blockContext;
    }

    /// <summary>
    /// Scan backwards from current line to determine what block we're in.
    /// Returns (blockContext, lastPropertyName if on a `prop =` line).
    /// </summary>
    private static (CompletionContext blockContext, string? lastPropName) DetermineBlockContext(
        string[] lines, int currentLine)
    {
        int depth = 0;
        bool inProject = false;
        bool inPackage = false;

        // Clamp currentLine to valid range
        int startLine = Math.Min(currentLine, lines.Length - 1);

        for (int i = startLine; i >= 0; i--)
        {
            var l = lines[i].Trim();

            // Remove comments
            var commentIdx = l.IndexOf("(*", StringComparison.Ordinal);
            if (commentIdx >= 0) l = l[..commentIdx].TrimEnd();
            var slashIdx = l.IndexOf("//", StringComparison.Ordinal);
            if (slashIdx >= 0) l = l[..slashIdx].TrimEnd();

            foreach (var c in l.Reverse())
            {
                if (c == '}') depth++;
                else if (c == '{') depth--;
            }

            if (depth < 0)
            {
                // We crossed a block opening — check what kind
                if (l.TrimStart().StartsWith("project ", StringComparison.OrdinalIgnoreCase) ||
                    l.TrimStart().StartsWith("project{", StringComparison.OrdinalIgnoreCase))
                    inProject = true;
                else if (l.TrimStart().StartsWith("package ", StringComparison.OrdinalIgnoreCase) ||
                         l.TrimStart() == "package{")
                    inPackage = true;
                break;
            }
        }

        if (depth == 0 && !inProject && !inPackage)
            return (new CompletionContext.TopLevel(), null);
        if (inProject)
            return (new CompletionContext.ProjectBlock(), null);
        if (inPackage)
            return (new CompletionContext.PackageBlock(), null);

        return (new CompletionContext.Unknown(), null);
    }

    private static string? GetWordAfterEquals(string lineUpToCursor)
    {
        var eqIdx = lineUpToCursor.LastIndexOf('=');
        if (eqIdx < 0) return null;
        var after = lineUpToCursor[(eqIdx + 1)..].Trim();
        // If there's already some content after =, we're completing an enum value
        return after.Length == 0 ? "" : after;
    }

    private static List<LspCompletionItem> MakeItems(IEnumerable<string> labels, int kind)
    {
        return labels.Select(l => new LspCompletionItem
        {
            Label = l,
            Kind  = kind,
        }).ToList();
    }
}
