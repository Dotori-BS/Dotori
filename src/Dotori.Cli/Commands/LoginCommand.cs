using System.CommandLine;
using Dotori.PackageManager.Config;

namespace Dotori.Cli.Commands;

internal static class LoginCommandFactory
{
    public static Command Create()
    {
        var command = new Command("login", "Save an authentication token for a registry");

        var registryOption = new Option<string?>("--registry") { Description = "Registry URL" };
        var tokenOption    = new Option<string?>("--token")    { Description = "Access token (reads from stdin if omitted)" };

        command.Add(registryOption);
        command.Add(tokenOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var config = DotoriConfigManager.Load();
            var registryUrl = parseResult.GetValue(registryOption) ?? config.DefaultRegistry;
            var token = parseResult.GetValue(tokenOption);

            if (token is null)
            {
                Console.Write($"Token for {registryUrl}: ");
                token = await ReadPasswordAsync(ct);
                Console.WriteLine();
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                Console.Error.WriteLine("error: token cannot be empty");
                return;
            }

            DotoriConfigManager.SetToken(registryUrl, token.Trim());
            Console.WriteLine($"Token saved for {registryUrl}");
        });

        return command;
    }

    private static async Task<string?> ReadPasswordAsync(CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            if (ct.IsCancellationRequested) break;
            var info = Console.ReadKey(intercept: true);
            if (info.Key == ConsoleKey.Enter) break;
            if (info.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Remove(sb.Length - 1, 1);
                continue;
            }
            sb.Append(info.KeyChar);
        }
        return await Task.FromResult(sb.ToString());
    }
}
