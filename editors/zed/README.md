# Dotori for Zed

This extension provides Tree-sitter syntax support and starts `dotori lsp` from
the active worktree's `PATH`. It recognizes `.dotori` and `name.dotori` files
and uses the shared language server for completion, diagnostics, hover,
navigation and formatting.

To format `.dotori` files on save, add this language-specific setting to Zed's
`settings.json` (the extension cannot change user settings):

```json
{
  "languages": {
    "Dotori": {
      "formatter": "language_server",
      "format_on_save": "on"
    }
  }
}
```

For local development, install this directory with **Zed: Install Dev
Extension**. Run `dotori setup ide --ide zed` in a project to generate C++
clangd and task configuration.

While the Dotori language server is running, it watches the worktree and updates
`compile_commands.json` and `.clangd` after saved `.dotori` changes or C/C++
source and header files are added, removed or renamed.

If Dotori is outside PATH, configure Zed's `settings.json` (paths containing
spaces are a single JSON string; do not add shell quotes):

```json
{
  "lsp": {
    "dotori-lsp": {
      "binary": {
        "path": "/absolute/path/to/dotori",
        "arguments": ["lsp"],
        "env": {}
      }
    }
  }
}
```

`arguments` defaults to `["lsp"]` and replaces the complete argument list when
set; the extension adds its file-watch option after these arguments. For a
managed build, use `"path": "dotnet"` and arguments
`["/absolute/path/to/dotori.dll", "lsp"]`. Restart the language server after
changing settings.

The Tree-sitter grammar is loaded from a fixed commit in this repository,
under `editors/zed/tree-sitter-dotori`; a separate grammar repository is not
required. To test local grammar changes, set `repository` in `extension.toml`
to a `file://` URL for your local Git checkout and `rev` to its committed SHA.
The grammar `path` stays unchanged. Restore the public URL before committing.

Build the LSP adapter with `cargo build --locked --release --target wasm32-wasip1`.
Zed builds the grammar when installing the dev extension and needs Git, Rust
and access to the grammar repository and WASI SDK. The extension has not been
published to the marketplace; publication requires an accepted license file.
