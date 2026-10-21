using System.Text;

namespace LinkMend.Core.Paths;

/// <summary>
/// Splits a Windows command line using the same backslash and quote rules as CommandLineToArgvW.
/// It is used only to read diagnostic arguments; LinkMend never executes the command line.
/// </summary>
public static class CommandLineSplitter
{
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return result;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        var index = 0;

        while (index < commandLine.Length)
        {
            var character = commandLine[index];
            if (character == '\\')
            {
                var slashCount = 0;
                while (index < commandLine.Length && commandLine[index] == '\\')
                {
                    slashCount++;
                    index++;
                }

                if (index < commandLine.Length && commandLine[index] == '"')
                {
                    current.Append('\\', slashCount / 2);
                    if (slashCount % 2 == 0)
                    {
                        inQuotes = !inQuotes;
                    }
                    else
                    {
                        current.Append('"');
                    }

                    index++;
                    hasToken = true;
                    continue;
                }

                current.Append('\\', slashCount);
                hasToken = true;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                index++;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                index++;
                continue;
            }

            current.Append(character);
            hasToken = true;
            index++;
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
