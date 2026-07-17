using System.Text;

namespace Octadock.Core.Ai;

internal static class UntrustedSourceFraming
{
    internal static void AppendIndentedSource(StringBuilder output, string text)
    {
        string[] lines = text.Replace('\v', '\n').ReplaceLineEndings("\n").Split('\n');
        foreach (string line in lines)
        {
            output.Append("    ").Append(line).Append('\n');
        }
    }
}
