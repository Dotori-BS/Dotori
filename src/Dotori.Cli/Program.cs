using System.CommandLine;
using Dotori.Cli;
using Dotori.Cli.Commands;

var rootCommand = new RootCommand("dotori — C++ build system and package manager");

rootCommand.Add(BuildCommandFactory.Create());
rootCommand.Add(RunCommandFactory.Create());
rootCommand.Add(TestCommandFactory.Create());
rootCommand.Add(WatchCommandFactory.Create());
rootCommand.Add(CleanCommandFactory.Create());
rootCommand.Add(InstallCommandFactory.CreateInstall());
rootCommand.Add(InstallCommandFactory.CreateUninstall());

// Package management commands (also available under `dotori package`)
rootCommand.Add(PackageCommandFactory.CreateAddCommand());
rootCommand.Add(PackageCommandFactory.CreateRemoveCommand());
rootCommand.Add(PackageCommandFactory.CreateUpdateCommand());
rootCommand.Add(PackageCommandFactory.CreateListCommand());
rootCommand.Add(PackageCommandFactory.Create());   // `dotori package ...` alias

rootCommand.Add(FormatCommandFactory.Create());
rootCommand.Add(InfoCommandFactory.Create());
rootCommand.Add(ExportCommandFactory.Create());
rootCommand.Add(SetupCommandFactory.Create());
rootCommand.Add(DoctorCommandFactory.Create());
rootCommand.Add(GenerateCompileCommandsCommandFactory.Create());
rootCommand.Add(LspCommandFactory.Create());

// Registry commands
rootCommand.Add(LoginCommandFactory.Create());
rootCommand.Add(LogoutCommandFactory.Create());
rootCommand.Add(PublishCommandFactory.Create());
rootCommand.Add(SearchCommandFactory.Create());

EnvFileLoader.Load();
return await rootCommand.Parse(args).InvokeAsync();
