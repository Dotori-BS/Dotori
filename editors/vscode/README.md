# Dotori for Visual Studio Code

Language support and project commands for the Dotori C++ build system.
Requires VS Code 1.95 or later and a separately installed Dotori executable.

## Install

Build from this directory with Node.js and npm:

```shell
npm ci
npm run check
npm test
npm run package
```

Choose **Extensions: Install from VSIX...** in VS Code and select
`dotori-0.1.0.vsix`. Open a project folder and a `.dotori` or `name.dotori`
file. The extension provides syntax highlighting, completion, diagnostics,
hover, definition/reference navigation and formatting through `dotori lsp`.
`.dotori` files are formatted automatically on save by default. Set
`dotori.formatOnSave` to `false` in VS Code settings to turn this off.

## Settings

If Dotori is outside PATH, set the executable path in VS Code settings:

```json
{
  "dotori.path": "C:/Tools/Dotori/dotori.exe"
}
```

For a managed build:

```json
{
  "dotori.path": "dotnet",
  "dotori.arguments": ["C:/Projects/Dotori/src/Dotori.Cli/bin/Release/net10.0/dotori.dll"]
}
```

`dotori.arguments` is a prefix shared by the language server and project
commands; do not add `lsp` there. Paths containing spaces are single strings
without extra shell quotes. These settings apply to the entire VS Code window.
Changing the executable, arguments or `dotori.languageServer.logFile` restarts
the language server automatically. Optional server logs require an absolute
file path writable by the server process. Client logs appear under **Output |
Dotori**.

## Commands

| Command palette entry | Action |
| --- | --- |
| Dotori: Build | `dotori build` |
| Dotori: Test | `dotori test` |
| Dotori: Run | `dotori run` |
| Dotori: Refresh IDE Files | `dotori setup ide --ide vscode` |
| Dotori: Format Current File | Format the active Dotori buffer through LSP |
| Dotori: Restart Language Server | Start a fresh LSP process with current settings |

Project commands save existing files first, then run as VS Code process tasks
with compiler diagnostics in the Problems panel. In a multi-root workspace,
the active file's workspace folder is used; otherwise choose a project folder.
Cancelling that choice or failing to save cancels the command. Formatting keeps
unsaved edits and supports untitled Dotori documents and Undo.

When a workspace contains a `.dotori` project, the language server also keeps
`compile_commands.json` and `.clangd` current after saved Dotori changes and
C/C++ source or header files are added, removed or renamed.

For C++ completion and debugging, run **Dotori: Refresh IDE Files** and install
the C++ extensions recommended by the generated workspace configuration.
This extension runs in the workspace extension host, including Remote SSH,
WSL and containers; install Dotori and configure paths in that environment.
It requires a trusted workspace with a filesystem and process execution.

The extension is packaged locally; it has not been published to the Marketplace.

## Integration test

From a Visual Studio Developer PowerShell, build the Release CLI, package the
extension, then run the installed VSIX in VS Code's real extension host:

```powershell
dotnet build ../../src/Dotori.Cli/Dotori.Cli.csproj -c Release
npm ci
npm run package
npm run test:integration
```

The first integration run downloads the official stable VS Code test runtime.
It installs the VSIX into a temporary profile, exercises language features and
project commands, and stores its result under `build/editor-integration`.
