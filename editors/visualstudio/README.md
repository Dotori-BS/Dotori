# Dotori for Visual Studio

This VSIX adds syntax highlighting and starts `dotori lsp` for `.dotori` files.
Format Document is provided by the LSP and can be run with **Edit | Advanced |
Format Document** (`Ctrl+K`, `Ctrl+D`). Visual Studio's LSP client does not
support `textDocument/willSaveWaitUntil`, so this extension cannot format Dotori
files automatically on save.
While the language server is running, it updates `compile_commands.json` and
`.clangd` after saved `.dotori` changes or C/C++ source and header files are
added, removed or renamed.

## Build

```powershell
dotnet build editors\visualstudio\Dotori.VisualStudio.csproj -c Release
```

Install the generated `.vsix` from `editors\visualstudio\bin\Release` and make
sure `dotori` is available on `PATH`. For the generated C/C++ solution itself,
run `dotori export build-system --format vcxproj` or `dotori setup ide
--ide visual-studio`.
