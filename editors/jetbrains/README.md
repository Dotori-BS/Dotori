# Dotori for JetBrains IDEs

This plugin recognizes `.dotori` files and starts `dotori lsp` through the
IntelliJ Platform LSP API. It targets the 2026.2 platform and is intended for
CLion and Rider, both of which provide the commercial LSP module.

The `dotori` executable must be available on the IDE process `PATH`. Use
`dotori setup ide --ide clion` or `--ide rider` to generate CMake Presets and
the C++ compilation database used by the IDE project model.

Build the plugin with:

```shell
./gradlew buildPlugin
```
