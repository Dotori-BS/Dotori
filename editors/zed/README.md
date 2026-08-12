# Dotori for Zed

This extension provides Tree-sitter syntax support and starts `dotori lsp` from
the active worktree's `PATH`.

For local development, install this directory with **Zed: Install Dev
Extension**. Run `dotori setup ide --ide zed` in a project to generate C++
clangd and task configuration.

The generated Tree-sitter source is kept in `tree-sitter-dotori/`. Before a
marketplace release, publish that directory as
`https://github.com/Dotori-BS/tree-sitter-dotori` and replace `rev = "main"`
in `extension.toml` with the published commit SHA. Zed marketplace submission
also requires an accepted license file in this directory.
