using System.Text;

namespace Dotori.Core.Executor;

/// <summary>Moves long process argument lists into a compiler response file.</summary>
public sealed class ResponseFileArguments : IDisposable
{
    private const int WindowsThreshold = 24 * 1024;
    private const int UnixThreshold = 96 * 1024;
    private readonly string? _path;

    private ResponseFileArguments(IReadOnlyList<string> arguments, string? path)
    {
        Arguments = arguments;
        _path = path;
    }

    public IReadOnlyList<string> Arguments { get; }

    public static ResponseFileArguments Create(
        IEnumerable<string> fragments, int? threshold = null, string? directory = null)
    {
        var arguments = ProcessArguments.Expand(fragments);
        var limit = threshold ?? (OperatingSystem.IsWindows() ? WindowsThreshold : UnixThreshold);
        var commandLength = arguments.Sum(argument => argument.Length + 1);
        if (commandLength <= limit)
            return new ResponseFileArguments(arguments, null);

        var path = Path.Combine(directory ?? Path.GetTempPath(), $"dotori-{Guid.NewGuid():N}.rsp");
        File.WriteAllLines(path, arguments.Select(Escape), new UTF8Encoding(false));
        return new ResponseFileArguments([$"@{path}"], path);
    }

    public void Dispose()
    {
        if (_path is null) return;
        try { File.Delete(_path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Escape(string argument)
    {
        if (argument.Length > 0 && !argument.Any(char.IsWhiteSpace) &&
            !argument.Contains('"') && !argument.Contains('\\'))
            return argument;

        var result = new StringBuilder(argument.Length + 2).Append('"');
        var slashes = 0;
        foreach (var ch in argument)
        {
            if (ch == '\\')
            {
                slashes++;
                continue;
            }
            if (ch == '"')
                result.Append('\\', slashes * 2 + 1).Append(ch);
            else
                result.Append('\\', slashes).Append(ch);
            slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('"');
        return result.ToString();
    }
}
