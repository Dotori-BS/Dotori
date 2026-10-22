using System.CommandLine;

namespace Dotori.Cli.Commands;

internal static class ExportCommandFactory
{
    public static Command Create()
    {
        var exportCommand = new Command("export", "Export project information");

        exportCommand.Add(IdeExportCommandFactory.Create());
        exportCommand.Add(ExportGrammarCommandFactory.Create());
        exportCommand.Add(ExportBuildSystemCommandFactory.Create());

        return exportCommand;
    }

}
