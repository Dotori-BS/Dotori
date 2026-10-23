using System.Text.RegularExpressions;
using Dotori.LanguageServer.Protocol;

namespace Dotori.LanguageServer.Providers;

/// <summary>
/// Provides context-aware completions for .dotori files.
/// The scanner is deliberately tolerant so completion also works while a block is incomplete.
/// </summary>
public static class CompletionProvider
{
    private const int KindKeyword = 14;
    private const int KindValue = 12;

    private static readonly string[] TopLevelKeywords = ["project", "package"];

    private static readonly string[] ProjectKeywords =
    [
        "type", "std", "description", "optimize", "debug-info", "runtime-link",
        "libc", "stdlib", "lto", "warnings", "warnings-as-errors",
        "android-api-level", "macos-min", "ios-min", "tvos-min", "watchos-min",
        "c-as-cpp", "objc-arc", "objc-as-objcpp", "toolchain", "manifest",
        "sources", "modules", "headers", "defines", "links", "frameworks",
        "compile-flags", "link-flags", "dependencies", "pch", "unity-build",
        "output", "pre-build", "post-build", "rule", "copy", "option",
        "emscripten-flags", "framework-paths", "resources", "assembler",
    ];

    private static readonly string[] PackageKeywords =
        ["name", "version", "description", "license", "homepage", "authors", "exports"];

    private static readonly string[] ConditionAtoms =
    [
        "windows", "linux", "macos", "ios", "tvos", "watchos", "android", "uwp", "wasm",
        "debug", "release", "msvc", "clang", "static", "dynamic", "glibc", "musl",
        "libcxx", "libstdcxx", "emscripten", "bare",
    ];

    private static readonly string[] BooleanValues = ["true", "false"];
    private static readonly string[] AssemblerTools = ["nasm", "yasm", "gas", "as", "masm", "auto"];

    private static readonly Dictionary<string, string[]> ValueCompletions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["type"] = ["executable", "test", "static-library", "shared-library", "header-only"],
        ["std"] = ["c++17", "c++20", "c++23"],
        ["optimize"] = ["none", "size", "speed", "full"],
        ["debug-info"] = ["none", "minimal", "full"],
        ["runtime-link"] = ["static", "dynamic"],
        ["libc"] = ["glibc", "musl"],
        ["stdlib"] = ["libc++", "libstdc++"],
        ["warnings"] = ["none", "default", "all", "extra"],
        ["lto"] = BooleanValues,
        ["warnings-as-errors"] = BooleanValues,
        ["c-as-cpp"] = BooleanValues,
        ["objc-arc"] = BooleanValues,
        ["objc-as-objcpp"] = BooleanValues,
    };

    private static readonly IReadOnlyDictionary<ScopeKind, string[]> ScopeKeywords =
        new Dictionary<ScopeKind, string[]>
        {
            [ScopeKind.Project] = ProjectKeywords,
            [ScopeKind.Condition] = ProjectKeywords,
            [ScopeKind.Package] = PackageKeywords,
            [ScopeKind.Sources] = ["include", "exclude"],
            [ScopeKind.Modules] = ["include", "exclude", "export-map"],
            [ScopeKind.Headers] = ["public", "private"],
            [ScopeKind.Pch] = ["header", "source", "modules"],
            [ScopeKind.UnityBuild] = ["enabled", "batch-size", "exclude"],
            [ScopeKind.Output] = ["binaries", "libraries", "symbols"],
            [ScopeKind.Option] = ["default", "defines", "dependencies"],
            [ScopeKind.Assembler] = ["tool", "format", "include", "exclude", "flags", "defines"],
            [ScopeKind.BuildRule] = ["command", "inputs", "outputs"],
            [ScopeKind.DependencyOptions] = ["git", "tag", "commit", "path", "version", "option"],
            [ScopeKind.Copy] = ["from"],
        };

    /// <summary>Compute completion items at the given LSP position.</summary>
    public static CompletionList GetCompletions(string text, int line, int character)
    {
        var offset = GetOffset(text, line, character);
        var analysis = Analyze(text, offset);

        IReadOnlyList<string> labels;
        var kind = KindKeyword;
        if (analysis.IsInString || analysis.IsInComment)
        {
            labels = [];
        }
        else if (analysis.ConditionDepth > 0)
        {
            labels = ConditionAtoms;
        }
        else if (TryGetAssignmentProperty(text, offset, analysis, out var property))
        {
            var scope = analysis.Scopes.Count == 0 ? ScopeKind.TopLevel : analysis.Scopes.Peek();
            labels = TryGetValueCompletions(scope, property, out var values) ? values : [];
            kind = KindValue;
        }
        else
        {
            var scope = analysis.Scopes.Count == 0 ? ScopeKind.TopLevel : analysis.Scopes.Peek();
            labels = ScopeKeywords.TryGetValue(scope, out var keywords)
                ? keywords
                : scope == ScopeKind.TopLevel ? TopLevelKeywords : [];
        }

        return new CompletionList
        {
            IsIncomplete = false,
            Items = MakeItems(labels, kind),
        };
    }

    private static bool TryGetValueCompletions(ScopeKind scope, string property, out string[] values)
    {
        if ((scope is ScopeKind.Project or ScopeKind.Condition) &&
            ValueCompletions.TryGetValue(property, out var projectValues))
        {
            values = projectValues;
            return true;
        }

        values = (scope, property.ToLowerInvariant()) switch
        {
            (ScopeKind.Option, "default") => BooleanValues,
            (ScopeKind.UnityBuild, "enabled") => BooleanValues,
            (ScopeKind.Modules, "export-map") => BooleanValues,
            (ScopeKind.Pch, "modules") => BooleanValues,
            (ScopeKind.Assembler, "tool") => AssemblerTools,
            _ => [],
        };
        return values.Length > 0;
    }

    private static bool TryGetAssignmentProperty(string text, int offset, Analysis analysis, out string property)
    {
        property = string.Empty;
        if (analysis.LastEqualsOffset < 0 || analysis.LastEqualsOffset <= analysis.LastBraceOffset)
            return false;

        var currentLineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        if (analysis.LastEqualsOffset < currentLineStart)
            return false;

        var beforeEquals = text[..analysis.LastEqualsOffset].TrimEnd();
        var match = Regex.Match(beforeEquals, @"(?<property>[A-Za-z_][A-Za-z0-9_-]*)$");
        if (!match.Success)
            return false;

        property = match.Groups["property"].Value;
        return true;
    }

    private static int GetOffset(string text, int line, int character)
    {
        if (line < 0 || character < 0)
            return 0;

        var lineStart = 0;
        for (var currentLine = 0; currentLine < line; currentLine++)
        {
            var newline = text.IndexOf('\n', lineStart);
            if (newline < 0)
                return text.Length;
            lineStart = newline + 1;
        }

        var lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0)
            lineEnd = text.Length;
        if (lineEnd > lineStart && text[lineEnd - 1] == '\r')
            lineEnd--;

        return lineStart + Math.Min(character, lineEnd - lineStart);
    }

    private static Analysis Analyze(string text, int offset)
    {
        var scopes = new Stack<ScopeKind>();
        var conditionDepth = 0;
        var blockCommentDepth = 0;
        var inString = false;
        var escaped = false;
        var inLineComment = false;
        var lastEqualsOffset = -1;
        var lastBraceOffset = -1;

        for (var i = 0; i < offset; i++)
        {
            var current = text[i];
            var next = i + 1 < offset ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                    inLineComment = false;
                continue;
            }

            if (blockCommentDepth > 0)
            {
                if (current == '(' && next == '*')
                {
                    blockCommentDepth++;
                    i++;
                }
                else if (current == '*' && next == ')')
                {
                    blockCommentDepth--;
                    i++;
                }
                continue;
            }

            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == '"')
                    inString = false;
                continue;
            }

            if (current == '#')
            {
                inLineComment = true;
                continue;
            }
            if (current == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }
            if (current == '(' && next == '*')
            {
                blockCommentDepth = 1;
                i++;
                continue;
            }
            if (current == '"')
            {
                inString = true;
                continue;
            }

            switch (current)
            {
                case '{':
                    scopes.Push(DetermineScope(
                        text,
                        i,
                        scopes.Count == 0 ? ScopeKind.TopLevel : scopes.Peek(),
                        lastBraceOffset));
                    lastBraceOffset = i;
                    break;
                case '}':
                    lastBraceOffset = i;
                    if (scopes.Count > 0)
                        scopes.Pop();
                    break;
                case '[':
                    conditionDepth++;
                    break;
                case ']':
                    conditionDepth = Math.Max(0, conditionDepth - 1);
                    break;
                case '=':
                    lastEqualsOffset = i;
                    break;
            }
        }

        return new Analysis(scopes, conditionDepth, inString, inLineComment || blockCommentDepth > 0,
            lastEqualsOffset, lastBraceOffset);
    }

    private static ScopeKind DetermineScope(
        string text,
        int braceOffset,
        ScopeKind parent,
        int previousBraceOffset)
    {
        var previousLineBreak = braceOffset == 0 ? -1 : text.LastIndexOf('\n', braceOffset - 1);
        var lineStart = Math.Max(previousLineBreak, previousBraceOffset) + 1;
        var header = text[lineStart..braceOffset].Trim();
        if (Regex.IsMatch(header, @"^project\s+\S+$", RegexOptions.IgnoreCase))
            return ScopeKind.Project;
        if (Regex.IsMatch(header, @"^package$", RegexOptions.IgnoreCase))
            return ScopeKind.Package;
        if (Regex.IsMatch(header, @"^\[[^\]]*\]$"))
            return ScopeKind.Condition;
        if (Regex.IsMatch(header, @"^option\s+\S+$", RegexOptions.IgnoreCase))
            return ScopeKind.Option;
        if (Regex.IsMatch(header, @"^rule\s+.+$", RegexOptions.IgnoreCase))
            return ScopeKind.BuildRule;
        if (header.EndsWith('=') && parent == ScopeKind.Dependencies)
            return ScopeKind.DependencyOptions;
        if (header.EndsWith('='))
            return ScopeKind.Freeform;

        return header.ToLowerInvariant() switch
        {
            "sources" => ScopeKind.Sources,
            "modules" => ScopeKind.Modules,
            "headers" => ScopeKind.Headers,
            "pch" => ScopeKind.Pch,
            "unity-build" => ScopeKind.UnityBuild,
            "output" => ScopeKind.Output,
            "option" => ScopeKind.Option,
            "assembler" => ScopeKind.Assembler,
            "dependencies" => ScopeKind.Dependencies,
            "copy" => ScopeKind.Copy,
            "defines" or "links" or "frameworks" or "framework-paths" or "compile-flags" or
                "link-flags" or "resources" or "emscripten-flags" or "pre-build" or "post-build" or
                "authors" or "exports" => ScopeKind.Freeform,
            _ => ScopeKind.Unknown,
        };
    }

    private static List<LspCompletionItem> MakeItems(IEnumerable<string> labels, int kind) =>
        labels.Select(label => new LspCompletionItem { Label = label, Kind = kind }).ToList();

    private sealed record Analysis(
        Stack<ScopeKind> Scopes,
        int ConditionDepth,
        bool IsInString,
        bool IsInComment,
        int LastEqualsOffset,
        int LastBraceOffset);

    private enum ScopeKind
    {
        TopLevel,
        Project,
        Package,
        Condition,
        Sources,
        Modules,
        Headers,
        Pch,
        UnityBuild,
        Output,
        Option,
        Assembler,
        BuildRule,
        Dependencies,
        DependencyOptions,
        Copy,
        Freeform,
        Unknown,
    }
}
