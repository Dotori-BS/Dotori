using System.CommandLine;

namespace Dotori.Cli.Commands;

internal static class GenerateCompileCommandsCommandFactory
{
    public static Command Create() => IdeExportCommandFactory.Create(legacy: true);
}
