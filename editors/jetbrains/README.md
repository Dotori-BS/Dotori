# Dotori for JetBrains IDEs

This plugin recognizes `.dotori` and `name.dotori` files, highlights the DSL,
and starts `dotori lsp` through the
IntelliJ Platform LSP API. It targets the 2026.2 platform and is intended for
CLion and Rider. Completion, diagnostics, hover, navigation and formatting
are provided by Dotori's shared language server.

Configure the executable under **Settings | Languages & Frameworks | Dotori**.
The default is `dotori` from the console environment's PATH. For an explicit
path such as `C:\Tools\Dotori\dotori.exe`, enter it without shell quotes.
Use a published Dotori executable; the plugin adds the `lsp` argument.
Restart the Dotori language server (or reopen the project) after changing it.

Use
`dotori setup ide --ide clion` or `--ide rider` to generate CMake Presets and
the C++ compilation database used by the IDE project model.

Build the plugin with JDK 25:

```shell
./gradlew test buildPlugin verifyPluginStructure verifyPluginProjectConfiguration
```

On Windows use `gradlew.bat`. Install `build/distributions/dotori-jetbrains-0.1.0.zip`
via **Settings | Plugins | gear icon | Install Plugin from Disk...** and restart
the IDE. Open a `.dotori` file to start the language server.

The plugin edits Dotori files. For C++ code completion and CMake project loading,
run the `setup ide` command above and open the generated `CMakeLists.txt`.
