namespace Dotori.Core.Generators;

/// <summary>Generates editor-specific task and settings files around Dotori's standard IDE outputs.</summary>
public static class IdeIntegrationGenerator
{
    public static IReadOnlyList<(string RelativePath, string Content)> Generate(string ide)
    {
        var normalized = ide.ToLowerInvariant();
        var files = new List<(string RelativePath, string Content)>();

        if (normalized is "all" or "auto" or "vscode")
        {
            files.Add((".vscode/settings.json", VscodeSettings));
            files.Add((".vscode/tasks.json", VscodeTasks));
            files.Add((".vscode/extensions.json", VscodeExtensions));
        }

        if (normalized is "all" or "auto" or "zed")
            files.Add((".zed/tasks.json", ZedTasks));

        return files;
    }

    private const string VscodeSettings = """
        {
          "cmake.useCMakePresets": "always",
          "clangd.arguments": [
            "--compile-commands-dir=${workspaceFolder}"
          ],
          "files.associations": {
            ".dotori": "dotori"
          }
        }
        """;

    private const string VscodeExtensions = """
        {
          "recommendations": [
            "dotori.dotori",
            "llvm-vs-code-extensions.vscode-clangd",
            "ms-vscode.cmake-tools"
          ]
        }
        """;

    private const string VscodeTasks = """
        {
          "version": "2.0.0",
          "tasks": [
            {
              "label": "dotori: build",
              "type": "process",
              "command": "dotori",
              "args": ["build"],
              "group": { "kind": "build", "isDefault": true }
            },
            {
              "label": "dotori: test",
              "type": "process",
              "command": "dotori",
              "args": ["test"],
              "group": "test"
            },
            {
              "label": "dotori: run",
              "type": "process",
              "command": "dotori",
              "args": ["run"]
            },
            {
              "label": "dotori: refresh IDE files",
              "type": "process",
              "command": "dotori",
              "args": ["setup", "ide", "--ide", "vscode"]
            }
          ]
        }
        """;

    private const string ZedTasks = """
        [
          {
            "label": "dotori: build",
            "command": "dotori build",
            "use_new_terminal": false,
            "allow_concurrent_runs": false
          },
          {
            "label": "dotori: test",
            "command": "dotori test",
            "use_new_terminal": false,
            "allow_concurrent_runs": false
          },
          {
            "label": "dotori: run",
            "command": "dotori run",
            "use_new_terminal": true,
            "allow_concurrent_runs": false
          },
          {
            "label": "dotori: refresh IDE files",
            "command": "dotori setup ide --ide zed",
            "use_new_terminal": false,
            "allow_concurrent_runs": false
          }
        ]
        """;
}
