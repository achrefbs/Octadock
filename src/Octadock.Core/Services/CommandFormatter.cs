using System.Text;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;

namespace Octadock.Core.Services;

/// <summary>
/// Default <see cref="ICommandFormatter"/>. Builds a canonical
/// <c>octadock://verb?query</c> URI from a <see cref="OctadockCommand"/>. Keys are
/// emitted in a stable (ordinal) order and values are percent-encoded so the
/// result round-trips through <see cref="CommandParser"/>.
/// </summary>
public sealed class CommandFormatter : ICommandFormatter
{
    /// <inheritdoc />
    public string ToUri(OctadockCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Type == CommandType.Unknown)
        {
            throw new ArgumentException("Cannot format an unknown command.", nameof(command));
        }

        string token = CommandTokens.ToToken(command.Type);
        var sb = new StringBuilder(CommandTokens.Scheme.Length + token.Length + 8);
        sb.Append(CommandTokens.Scheme).Append("://").Append(token);

        if (command.Parameters.Count > 0)
        {
            sb.Append('?');
            bool first = true;
            foreach (KeyValuePair<string, string> pair in command.Parameters
                         .OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!first)
                {
                    sb.Append('&');
                }

                sb.Append(Uri.EscapeDataString(pair.Key))
                  .Append('=')
                  .Append(Uri.EscapeDataString(pair.Value ?? string.Empty));
                first = false;
            }
        }

        return sb.ToString();
    }
}
