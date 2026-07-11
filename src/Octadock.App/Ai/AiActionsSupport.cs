using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Octadock.Core.Ai;
using Octadock.Core.Io;

namespace Octadock.App.Ai;

/// <summary>Explicit confirmation seam used immediately before a reviewed send.</summary>
public interface IAiSendConfirmation
{
    bool Confirm(AiOutboundReview review);
}

/// <summary>Standard WPF confirmation that names the selected destination and payload size.</summary>
[SupportedOSPlatform("windows")]
public sealed class WpfAiSendConfirmation : IAiSendConfirmation
{
    public bool Confirm(AiOutboundReview review)
    {
        ArgumentNullException.ThrowIfNull(review);

        string secretLine = review.DetectedSecretCount switch
        {
            0 => "No common secrets were detected.",
            _ when review.SecretsRedacted =>
                $"{review.DetectedSecretCount:N0} detected secret(s) are redacted in that preview.",
            _ => $"Warning: {review.DetectedSecretCount:N0} detected secret(s) will be sent without redaction.",
        };
        string message =
            $"Send the reviewed preview to {review.ProviderDisplayName}?\n\n" +
            $"Exactly {review.OutboundCharacterCount:N0} characters will be sent to {review.DestinationDisclosure}.\n\n" +
            $"{secretLine}\n\n" +
            "Octadock does not save the source or result. Continue?";

        MessageBoxImage icon = review.DetectedSecretCount > 0 && !review.SecretsRedacted
            ? MessageBoxImage.Warning
            : MessageBoxImage.Question;
        return MessageBox.Show(
                   message,
                   $"Send to {review.ProviderDisplayName}",
                   MessageBoxButton.YesNo,
                   icon,
                   MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}

public sealed record AiTextFileInput(string Text, string SourceName);

public interface IAiTextFileLoader
{
    Task<AiTextFileInput> LoadAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Streams a bounded local text file without following UNC/network paths.</summary>
public sealed class AiTextFileLoader : IAiTextFileLoader
{
    public async Task<AiTextFileInput> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Choose a local text file.", nameof(path));
        }

        if (PathSafety.IsUncPath(path))
        {
            throw new InvalidOperationException("AI Actions only opens local text files, not network paths.");
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The selected text file could not be found.", fullPath);
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = new StringBuilder(Math.Min((int)Math.Min(stream.Length, 32_768), AiTextActionLimits.MaxInputCharacters));
        char[] buffer = new char[8 * 1024];
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.AsSpan(0, read).Contains('\0'))
            {
                throw new InvalidOperationException("The selected file appears to be binary, not text.");
            }

            if (text.Length + read > AiTextActionLimits.MaxInputCharacters)
            {
                throw new InvalidOperationException(
                    $"AI Actions accepts up to {AiTextActionLimits.MaxInputCharacters:N0} characters at a time.");
            }

            text.Append(buffer, 0, read);
        }

        return new AiTextFileInput(text.ToString(), Path.GetFileName(fullPath));
    }
}

public interface IAiTextFilePicker
{
    string? PickFile();
}

/// <summary>Native local-file picker for supported text-like artifacts.</summary>
[SupportedOSPlatform("windows")]
public sealed class WpfAiTextFilePicker : IAiTextFilePicker
{
    public string? PickFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open text for AI Actions",
            CheckFileExists = true,
            Multiselect = false,
            Filter =
                "Text artifacts|*.txt;*.md;*.markdown;*.json;*.csv;*.log;*.xml;*.yaml;*.yml;*.cs;*.ts;*.tsx;*.js;*.jsx;*.py;*.sql;*.html;*.css|All files|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
