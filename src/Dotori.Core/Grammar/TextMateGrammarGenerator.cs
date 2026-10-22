namespace Dotori.Core.Grammar;

/// <summary>TextMate 형식 (.tmLanguage.json) 문법 파일 생성기.</summary>
/// <remarks>VSCode, JetBrains/IntelliJ, Sublime Text, TextMate, Atom 등에서 사용합니다.</remarks>
public sealed class TextMateGrammarGenerator : IGrammarGenerator
{
    public string FormatId => "textmate";
    public string DefaultExtension => ".tmLanguage.json";

    // JSON 내 정규식에서 백슬래시는 두 번 이스케이프 필요: \\b → JSON에서 \\\\b
    private const string Template = """
        {
          "$schema": "https://raw.githubusercontent.com/martinring/tmlanguage/master/tmlanguage.json",
          "name": "Dotori",
          "scopeName": "source.dotori",
          "fileTypes": ["dotori"],
          "patterns": [
            { "include": "#comments-line"  },
            { "include": "#comments-block" },
            { "include": "#top-keywords"   },
            { "include": "#block-keywords" },
            { "include": "#property-keywords" },
            { "include": "#value-keywords" },
            { "include": "#condition-blocks" },
            { "include": "#strings"        },
            { "include": "#numbers"        },
            { "include": "#operators"      },
            { "include": "#identifiers"    }
          ],
          "repository": {
            "comments-line": {
              "patterns": [
                { "name": "comment.line.number-sign.dotori",   "match": "#.*$"  },
                { "name": "comment.line.double-slash.dotori",  "match": "//.*$" }
              ]
            },
            "comments-block": {
              "name": "comment.block.dotori",
              "begin": "\\(\\*",
              "end":   "\\*\\)",
              "captures": { "0": { "name": "punctuation.definition.comment.dotori" } }
            },
            "top-keywords": {
              "name":  "keyword.declaration.dotori",
              "match": "(?<![a-zA-Z0-9_+\\-])({{TOP_KEYWORDS}})(?![a-zA-Z0-9_+\\-])"
            },
            "block-keywords": {
              "name":  "keyword.other.block.dotori",
              "match": "(?<![a-zA-Z0-9_+\\-])({{BLOCK_KEYWORDS}})(?![a-zA-Z0-9_+\\-])"
            },
            "property-keywords": {
              "name":  "support.type.property-name.dotori",
              "match": "(?<![a-zA-Z0-9_+\\-])({{PROPERTY_KEYWORDS}})(?![a-zA-Z0-9_+\\-])"
            },
            "value-keywords": {
              "patterns": [
                {
                  "name":  "support.constant.type.dotori",
                  "match": "(?<![a-zA-Z0-9_+\\-])({{TYPE_VALUES}})(?![a-zA-Z0-9_+\\-])"
                },
                {
                  "name":  "constant.language.std.dotori",
                  "match": "(?<![a-zA-Z0-9_+\\-])({{STD_VALUES}})(?![a-zA-Z0-9_+\\-])"
                },
                {
                  "name":  "constant.language.dotori",
                  "match": "(?<![a-zA-Z0-9_+\\-])({{ENUM_VALUES}})(?![a-zA-Z0-9_+\\-])"
                }
              ]
            },
            "condition-blocks": {
              "name":  "meta.condition.dotori",
              "begin": "\\[",
              "end":   "\\]",
              "beginCaptures": { "0": { "name": "punctuation.section.condition.begin.dotori" } },
              "endCaptures":   { "0": { "name": "punctuation.section.condition.end.dotori"   } },
              "patterns": [
                { "name": "keyword.control.platform.dotori",  "match": "(?<![a-zA-Z0-9_+\\-])({{PLATFORM_CONDS}})(?![a-zA-Z0-9_+\\-])" },
                { "name": "keyword.control.config.dotori",    "match": "(?<![a-zA-Z0-9_+\\-])({{CONFIG_CONDS}})(?![a-zA-Z0-9_+\\-])" },
                { "name": "keyword.control.compiler.dotori",  "match": "(?<![a-zA-Z0-9_+\\-])({{COMPILER_CONDS}})(?![a-zA-Z0-9_+\\-])" },
                { "name": "keyword.control.runtime.dotori",   "match": "(?<![a-zA-Z0-9_+\\-])({{RUNTIME_CONDS}})(?![a-zA-Z0-9_+\\-])" },
                { "name": "punctuation.separator.condition.dotori", "match": "\\." }
              ]
            },
            "strings": {
              "name":  "string.quoted.double.dotori",
              "begin": "\"",
              "end":   "\"",
              "patterns": [
                { "name": "variable.other.env.dotori",         "match": "\\$\\{[^}]*\\}" },
                { "name": "constant.character.escape.dotori",  "match": "\\\\."          }
              ]
            },
            "numbers": {
              "name":  "constant.numeric.integer.dotori",
              "match": "\\b\\d+\\b"
            },
            "operators": {
              "name":  "keyword.operator.assignment.dotori",
              "match": "="
            },
            "identifiers": {
              "name":  "variable.other.dotori",
              "match": "[a-zA-Z_][a-zA-Z0-9_+\\-]*"
            }
          }
        }
        """;

    public string Generate()
    {
        var vars = new Dictionary<string, string>
        {
            ["TOP_KEYWORDS"]      = Alt(DotoriGrammarDefinition.TopLevelKeywords),
            ["BLOCK_KEYWORDS"]    = Alt(DotoriGrammarDefinition.BlockKeywords),
            ["PROPERTY_KEYWORDS"] = Alt(DotoriGrammarDefinition.PropertyKeywords),
            ["TYPE_VALUES"]       = Alt(DotoriGrammarDefinition.TypeValues),
            ["STD_VALUES"]        = Alt(DotoriGrammarDefinition.StdValues, escape: true),
            ["ENUM_VALUES"]       = Alt(DotoriGrammarDefinition.EnumValues, escape: true),
            ["PLATFORM_CONDS"]    = Alt(DotoriGrammarDefinition.PlatformConditions),
            ["CONFIG_CONDS"]      = Alt(DotoriGrammarDefinition.ConfigConditions),
            ["COMPILER_CONDS"]    = Alt(DotoriGrammarDefinition.CompilerConditions),
            ["RUNTIME_CONDS"]     = Alt(DotoriGrammarDefinition.RuntimeConditions),
        };
        return TemplateEngine.Render(Template, vars);
    }

    /// <summary>키워드 목록을 정규식 교대 패턴으로 결합합니다.</summary>
    private static string Alt(IEnumerable<string> keywords, bool escape = false) =>
        string.Join("|", escape ? keywords.Select(EscapeRegex) : keywords);

    private static string EscapeRegex(string s) =>
        s.Replace("+", "\\\\+").Replace(".", "\\\\.");
}
