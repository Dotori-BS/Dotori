namespace Dotori.Cli;

internal static class EnvFileLoader
{
    private const string EnvFileName = ".env";

    public static void Load(string? startDirectory = null)
    {
        var dir = startDirectory ?? Directory.GetCurrentDirectory();
        var envPath = FindEnvFile(dir);
        if (envPath is null) return;

        foreach (var line in File.ReadLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;

            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();

            // 작은따옴표는 변수 치환 없이 리터럴로 처리
            bool singleQuoted = value.Length >= 2 && value[0] == '\'' && value[^1] == '\'';
            if (singleQuoted)
            {
                value = value[1..^1];
            }
            else
            {
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];

                value = ExpandVariables(value);
            }

            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string ExpandVariables(string value)
    {
        if (!value.Contains("${")) return value;

        var result = new System.Text.StringBuilder();
        var i = 0;
        while (i < value.Length)
        {
            if (i + 1 < value.Length && value[i] == '$' && value[i + 1] == '{')
            {
                var close = value.IndexOf('}', i + 2);
                if (close > i + 2)
                {
                    var varName = value[(i + 2)..close];
                    result.Append(Environment.GetEnvironmentVariable(varName) ?? string.Empty);
                    i = close + 1;
                    continue;
                }
            }
            result.Append(value[i++]);
        }
        return result.ToString();
    }

    private static string? FindEnvFile(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, EnvFileName);
            if (File.Exists(candidate)) return candidate;

            if (dir.GetFiles("*.dotori").Length > 0) break;

            dir = dir.Parent;
        }
        return null;
    }
}
