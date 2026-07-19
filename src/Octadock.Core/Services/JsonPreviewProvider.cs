using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>
/// Preserves original JSON source and exposes pretty JSON as a separate rendered
/// payload. Parse diagnostics remain structured and never enter source content.
/// </summary>
public sealed class JsonPreviewProvider : IFilePreviewProvider
{
    internal const int MaxFormatBytes = 2 * 1024 * 1024;
    internal const int MaxRenderedBytes = 4 * 1024 * 1024;

    public int Priority => 1;

    public bool CanPreview(string extension)
        => extension is ".json" or ".jsonc";

    public async Task<FilePreviewResult> LoadAsync(
        string path,
        FilePreviewOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            PreviewTextReadResult read = await PreviewTextReader
                .ReadHeadAsync(path, MaxFormatBytes, cancellationToken, allowControlCharacters: true)
                .ConfigureAwait(false);

            if (read.SourceByteLength == 0)
            {
                return SourceResult(path, read);
            }

            if (read.Scope.IsTruncated)
            {
                return SourceResult(
                    path,
                    read,
                    new FilePreviewFailure(
                        FilePreviewFailureKind.TooLarge,
                        "This JSON file is too large to format. Showing the original source window."));
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(read.Text, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                string formatted;
                try
                {
                    formatted = FormatBounded(document.RootElement);
                }
                catch (RenderedJsonTooLargeException)
                {
                    return SourceResult(
                        path,
                        read,
                        new FilePreviewFailure(
                            FilePreviewFailureKind.TooLarge,
                            "Formatted JSON exceeds the safe preview limit. Showing the original source."));
                }

                return new FilePreviewResult
                {
                    Kind = FilePreviewKind.PlainText,
                    FilePath = path,
                    SourceContent = read.Text,
                    RenderedContent = formatted,
                    SourceByteLength = read.SourceByteLength,
                    DetectedEncoding = read.DetectedEncoding,
                    Scope = read.Scope,
                    Warnings = read.Warnings,
                };
            }
            catch (JsonException ex)
            {
                string location = ex.LineNumber is long line
                    ? string.Create(CultureInfo.InvariantCulture, $"Invalid JSON near line {line + 1}.")
                    : "Invalid JSON.";
                return SourceResult(
                    path,
                    read,
                    new FilePreviewFailure(
                        FilePreviewFailureKind.Malformed,
                        "This JSON isn't valid. Showing the original text."),
                    new FilePreviewWarning(FilePreviewWarningKind.Malformed, location));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PreviewFailureMapper.FromException(path, ex);
        }
    }

    private static FilePreviewResult SourceResult(
        string path,
        PreviewTextReadResult read,
        FilePreviewFailure? failure = null,
        FilePreviewWarning? additionalWarning = null)
    {
        IReadOnlyList<FilePreviewWarning> warnings = additionalWarning is null
            ? read.Warnings
            : [.. read.Warnings, additionalWarning];
        return new FilePreviewResult
        {
            Kind = FilePreviewKind.PlainText,
            FilePath = path,
            SourceContent = read.Text,
            SourceByteLength = read.SourceByteLength,
            DetectedEncoding = read.DetectedEncoding,
            Scope = read.Scope,
            Warnings = warnings,
            Failure = failure,
        };
    }

    private static string FormatBounded(JsonElement element)
    {
        var buffer = new CappedByteBufferWriter(MaxRenderedBytes);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            element.WriteTo(writer);
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private sealed class CappedByteBufferWriter : IBufferWriter<byte>
    {
        private readonly byte[] _buffer;
        private int _written;

        public CappedByteBufferWriter(int capacity) => _buffer = new byte[capacity];

        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length - _written)
            {
                throw new RenderedJsonTooLargeException();
            }

            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            int required = Math.Max(1, sizeHint);
            if (required > _buffer.Length - _written)
            {
                throw new RenderedJsonTooLargeException();
            }

            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
    }

    private sealed class RenderedJsonTooLargeException : Exception;
}
