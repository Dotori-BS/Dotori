using System.Diagnostics;
using System.Text;

namespace Dotori.Core.Executor;

/// <summary>Expands legacy argument fragments into an injection-safe ArgumentList.</summary>
public static class ProcessArguments
{
    public static IReadOnlyList<string> Expand(IEnumerable<string> fragments)
    {
        var result = new List<string>();
        foreach (var fragment in fragments)
            Tokenize(fragment, result);
        return result;
    }

    public static void AddTo(ProcessStartInfo startInfo, IEnumerable<string> fragments)
    {
        foreach (var argument in Expand(fragments))
            startInfo.ArgumentList.Add(argument);
    }

    private static void Tokenize(string value, List<string> output)
    {
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (ch == '\\' && index + 1 < value.Length && value[index + 1] == '"')
            {
                current.Append('"');
                index++;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !quoted)
            {
                Flush(current, output);
                continue;
            }
            current.Append(ch);
        }
        Flush(current, output);
    }

    private static void Flush(StringBuilder current, List<string> output)
    {
        if (current.Length == 0) return;
        output.Add(current.ToString());
        current.Clear();
    }
}
