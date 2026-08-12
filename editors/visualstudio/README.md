# Dotori for Visual Studio

This VSIX adds syntax highlighting and starts `dotori lsp` for `.dotori` files.

## Build

```powershell
dotnet build editors\visualstudio\Dotori.VisualStudio.csproj -c Release
```

Install the generated `.vsix` from `editors\visualstudio\bin\Release` and make
sure `dotori` is available on `PATH`. For the generated C/C++ solution itself,
run `dotori export build-system --format vcxproj` or `dotori setup ide
--ide visual-studio`.
