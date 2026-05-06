using System.CommandLine;
using Dotori.PackageManager;
using Dotori.PackageManager.Config;

namespace Dotori.Cli.Commands;

internal static class SearchCommandFactory
{
    public static Command Create()
    {
        var command = new Command("search", "Search for packages in an npm-compatible registry");

        var queryArg       = new Argument<string>("query")      { Description = "Search query" };
        var registryOption = new Option<string?>("--registry")  { Description = "Registry URL" };
        var sizeOption     = new Option<int?>("--size")         { Description = "Results per page (default: 20)" };
        var fromOption     = new Option<int?>("--from")         { Description = "Offset (default: 0)" };

        command.Add(queryArg);
        command.Add(registryOption);
        command.Add(sizeOption);
        command.Add(fromOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var query  = parseResult.GetValue(queryArg) ?? "";
            var regUrl = parseResult.GetValue(registryOption);
            var size   = parseResult.GetValue(sizeOption) ?? 20;
            var from   = parseResult.GetValue(fromOption) ?? 0;

            var config = DotoriConfigManager.Load();
            var resolvedUrl = regUrl ?? config.DefaultRegistry;
            var reg = config.GetRegistry(resolvedUrl);

            using var client = new NpmRegistryClient(resolvedUrl, reg.Token);
            var result = await client.SearchAsync(query, size, from, ct);

            if (result.Total == 0)
            {
                Console.WriteLine($"No packages found for '{query}'");
                return;
            }

            Console.WriteLine($"Found {result.Total} package(s) matching '{query}':\n");
            Console.WriteLine($"{"NAME",-40} {"VERSION",-12}");
            Console.WriteLine(new string('-', 55));
            foreach (var obj in result.Objects)
            {
                var pkg = obj.Package;
                if (pkg is null) continue;
                Console.WriteLine($"{pkg.Name,-40} {pkg.Version,-12}");
                if (pkg.Description is not null)
                    Console.WriteLine($"  {pkg.Description}");
            }
        });

        return command;
    }
}
