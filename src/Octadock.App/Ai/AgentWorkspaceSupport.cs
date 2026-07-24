using System.Globalization;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Ai;
using Octadock.Core.Context;
using Octadock.Core.Imaging;
using Octadock.Core.Io;
using Octadock.Core.Models;
using SkiaSharp;

namespace Octadock.App.Ai;

/// <summary>One local evidence item selected for an Agent Packet.</summary>
public sealed partial class AgentWorkspaceEvidence : ObservableObject
{
    [ObservableProperty]
    private bool _isIncluded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailLabel))]
    private string? _textContent;

    public required Guid Id { get; init; }

    public required AgentPacketSourceKind Kind { get; init; }

    public required string Label { get; init; }

    public required AgentPacketProvenance Provenance { get; init; }

    /// <summary>Absolute local path used only while materializing the reviewed bundle.</summary>
    public string? LocalPath { get; init; }

    /// <summary>Safe path shown to the agent, relative to the exported bundle.</summary>
    public string? RelativeAssetPath { get; init; }

    public string? MediaType { get; init; }

    public string? Sha256 { get; init; }

    public int? PixelWidth { get; init; }

    public int? PixelHeight { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>True only for a temporary copy created and owned by Agent Workspace.</summary>
    public bool IsOwnedTemporary { get; init; }

    public bool HasPixels => PixelWidth is > 0 && PixelHeight is > 0;

    public bool CanRunOcr =>
        HasPixels &&
        LocalPath is not null &&
        Provenance.Kind != AgentPacketProvenanceKind.VisualVerification;

    public string SourceId => $"src-{Id:N}";

    public string KindLabel => Kind switch
    {
        AgentPacketSourceKind.Capture => "Capture",
        AgentPacketSourceKind.Context => "Context",
        AgentPacketSourceKind.Image => "Image",
        AgentPacketSourceKind.File => "File",
        AgentPacketSourceKind.VisualDiff => "Verification",
        _ => Provenance.Kind == AgentPacketProvenanceKind.Clipboard ? "Clipboard" : "Note",
    };

    public string DetailLabel
    {
        get
        {
            var details = new List<string>();
            if (HasPixels)
            {
                details.Add($"{PixelWidth} × {PixelHeight}");
            }

            if (SizeBytes > 0)
            {
                details.Add(FormatBytes(SizeBytes));
            }

            if (!string.IsNullOrWhiteSpace(TextContent))
            {
                details.Add($"{TextContent.Length:N0} text chars");
            }

            return details.Count == 0 ? Provenance.Kind.ToString() : string.Join("  ·  ", details);
        }
    }

    public AgentPacketSourceItem ToPacketSource()
    {
        if (RelativeAssetPath is null)
        {
            return Kind == AgentPacketSourceKind.Context
                ? AgentPacketSourceItem.CreateContext(SourceId, Label, TextContent ?? string.Empty, Provenance)
                : AgentPacketSourceItem.CreateText(SourceId, Label, TextContent ?? string.Empty, Provenance);
        }

        string role = Kind switch
        {
            AgentPacketSourceKind.Capture => AgentPacketAssetRoles.Capture,
            AgentPacketSourceKind.Image => AgentPacketAssetRoles.Image,
            AgentPacketSourceKind.Context => AgentPacketAssetRoles.File,
            AgentPacketSourceKind.VisualDiff => AgentPacketAssetRoles.Diff,
            _ => AgentPacketAssetRoles.File,
        };
        return new AgentPacketSourceItem
        {
            Id = SourceId,
            Kind = Kind,
            Label = Label,
            Provenance = Provenance,
            TextContent = TextContent,
            Assets =
            [
                new AgentPacketAssetReference
                {
                    Role = role,
                    RelativePath = RelativeAssetPath,
                    MediaType = MediaType,
                    Sha256 = Sha256,
                    PixelWidth = PixelWidth,
                    PixelHeight = PixelHeight,
                },
            ],
        };
    }

    private static string FormatBytes(long bytes)
    {
        double value = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB"];
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return index == 0 ? $"{value:0} {units[index]}" : $"{value:0.#} {units[index]}";
    }
}

/// <summary>Builds bounded local evidence descriptors without retaining file bytes.</summary>
public sealed class AgentEvidenceFactory
{
    internal const long MaxEvidenceBytes = 128L * 1024 * 1024;
    internal const long MaxPacketAttachmentBytes = 256L * 1024 * 1024;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif",
    };
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".csv", ".log", ".xml", ".yaml", ".yml",
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".sql", ".html", ".css", ".toml",
    };

    private readonly IStoragePaths _paths;
    private readonly ISafeFileWriter _safeWriter;

    public AgentEvidenceFactory(
        IAiTextFileLoader textFiles,
        IStoragePaths paths,
        ISafeFileWriter safeWriter)
    {
        ArgumentNullException.ThrowIfNull(textFiles);
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _safeWriter = safeWriter ?? throw new ArgumentNullException(nameof(safeWriter));
    }

    public AgentWorkspaceEvidence CreateText(
        string label,
        string text,
        AgentPacketProvenanceKind provenanceKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new AgentWorkspaceEvidence
        {
            Id = Guid.NewGuid(),
            Kind = AgentPacketSourceKind.Text,
            Label = label.Trim(),
            TextContent = text,
            Provenance = new AgentPacketProvenance { Kind = provenanceKind },
        };
    }

    public Task<AgentWorkspaceEvidence> FromCaptureAsync(
        CaptureRecord capture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        string path = _paths.ToAbsolute(capture.OriginalPath);
        return FromFileAsync(
            path,
            AgentPacketSourceKind.Capture,
            Path.GetFileName(capture.OriginalPath),
            new AgentPacketProvenance
            {
                Kind = AgentPacketProvenanceKind.ScreenCapture,
                ApplicationName = capture.Source.ProcessName,
                WindowTitle = capture.Source.WindowTitle,
                Reference = $"capture:{capture.Id:N}",
                CapturedAt = capture.CreatedAt,
            },
            cancellationToken: cancellationToken);
    }

    public async Task<AgentWorkspaceEvidence> FromContextItemAsync(
        ContextPackage package,
        ContextItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(item);
        string path = item.Ownership == ContextOwnership.Reference
            ? item.ReferenceSourcePath ?? throw new InvalidOperationException("The Context reference has no source path.")
            : string.IsNullOrWhiteSpace(item.StorageRelativePath)
                ? throw new InvalidOperationException("The Context snapshot has no managed path.")
                : _paths.ToAbsolute(item.StorageRelativePath);

        AgentWorkspaceEvidence evidence = await FromFileAsync(
            path,
            AgentPacketSourceKind.Context,
            $"{package.Name} / {item.DisplayName}",
            new AgentPacketProvenance
            {
                Kind = AgentPacketProvenanceKind.ContextPackage,
                Reference = $"context:{package.Id:N}/{item.Id:N}",
                CapturedAt = item.AddedAt,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (item.Ownership == ContextOwnership.Reference)
        {
            if (evidence.SizeBytes != item.SizeBytes ||
                string.IsNullOrWhiteSpace(item.ReferenceSha256) ||
                !string.Equals(evidence.Sha256, item.ReferenceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Referenced Context item '{item.DisplayName}' changed after it was added. Remove it and add it again before handoff.");
            }
        }

        return evidence;
    }

    public async Task<AgentWorkspaceEvidence> FromClipboardImageAsync(
        EncodedImage image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Bytes.Length <= 0 || image.Bytes.Length > MaxEvidenceBytes)
        {
            throw new InvalidOperationException(
                $"Clipboard images must be between 1 byte and {MaxEvidenceBytes / (1024 * 1024):N0} MB.");
        }

        string tempRoot = AgentLocalPathGuard.ValidateDestinationDirectory(
            _paths.TempExportsDirectory,
            "Agent Workspace temporary storage");
        Directory.CreateDirectory(tempRoot);
        AgentLocalPathGuard.ValidateExistingDirectory(tempRoot, "Agent Workspace temporary storage");
        string directory = AgentLocalPathGuard.ValidateDestinationDirectory(
            Path.Combine(tempRoot, "AgentWorkspace", "Clipboard"),
            "Agent Workspace clipboard storage");
        Directory.CreateDirectory(directory);
        AgentLocalPathGuard.ValidateExistingDirectory(directory, "Agent Workspace clipboard storage");
        string path = Path.Combine(directory, $"clipboard-{Guid.NewGuid():N}{image.Extension}");
        try
        {
            await _safeWriter.WriteAsync(path, image.Bytes, cancellationToken).ConfigureAwait(false);
            return await FromFileAsync(
                path,
                AgentPacketSourceKind.Image,
                "Clipboard image",
                new AgentPacketProvenance { Kind = AgentPacketProvenanceKind.Clipboard },
                ownedTemporary: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            DeleteOwnedTemporaryFile(path);
            throw;
        }
    }

    public async Task<AgentWorkspaceEvidence> FromFileAsync(
        string path,
        AgentPacketSourceKind requestedKind = AgentPacketSourceKind.File,
        string? label = null,
        AgentPacketProvenance? provenance = null,
        bool ownedTemporary = false,
        CancellationToken cancellationToken = default)
    {
        string fullPath = ValidateLocalFile(path);
        var info = new FileInfo(fullPath);
        if (info.Length is <= 0 or > MaxEvidenceBytes)
        {
            throw new InvalidOperationException(
                $"Evidence files must be between 1 byte and {MaxEvidenceBytes / (1024 * 1024):N0} MB.");
        }

        string extension = Path.GetExtension(fullPath);
        bool isImage = ImageExtensions.Contains(extension);
        string? text = null;
        string hash;
        int? width;
        int? height;
        long inspectedLength;
        if (!isImage && TextExtensions.Contains(extension) && info.Length <= 2L * 1024 * 1024)
        {
            StableTextInspection inspected = await InspectTextFileAsync(fullPath, cancellationToken)
                .ConfigureAwait(false);
            text = inspected.Text;
            hash = inspected.Sha256;
            width = null;
            height = null;
            inspectedLength = inspected.Length;
        }
        else
        {
            (hash, width, height, inspectedLength) = await Task.Run(
                () => InspectFile(fullPath, isImage, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        Guid id = Guid.NewGuid();
        string safeExtension = SafeExtension(extension);
        AgentPacketSourceKind kind = requestedKind switch
        {
            AgentPacketSourceKind.Context when isImage => AgentPacketSourceKind.Image,
            AgentPacketSourceKind.Context when text is null => AgentPacketSourceKind.File,
            AgentPacketSourceKind.File when isImage => AgentPacketSourceKind.Image,
            // A standalone heat map is an image source. A true visual-diff
            // source needs before + after (+ optional diff) in one packet item.
            AgentPacketSourceKind.VisualDiff => AgentPacketSourceKind.Image,
            _ => requestedKind,
        };
        // Reviewed text is carried inside TASK.md instead of attaching the
        // original file. Otherwise a redacted preview could sit beside an
        // unredacted source file that the agent is still able to read.
        string? relativeAssetPath = text is null
            ? $"assets/src-{id:N}{safeExtension}"
            : null;
        return new AgentWorkspaceEvidence
        {
            Id = id,
            Kind = kind,
            Label = string.IsNullOrWhiteSpace(label) ? Path.GetFileName(fullPath) : label.Trim(),
            LocalPath = fullPath,
            RelativeAssetPath = relativeAssetPath,
            MediaType = MediaType(extension, isImage),
            Sha256 = hash,
            PixelWidth = width,
            PixelHeight = height,
            SizeBytes = inspectedLength,
            IsOwnedTemporary = ownedTemporary,
            TextContent = text,
            Provenance = provenance ?? new AgentPacketProvenance
            {
                Kind = AgentPacketProvenanceKind.FileSystem,
                Reference = Path.GetFileName(fullPath),
            },
        };
    }

    private static string ValidateLocalFile(string path)
    {
        return AgentLocalPathGuard.ValidateExistingFile(path, "Agent evidence file");
    }

    private static (string Hash, int? Width, int? Height, long Length) InspectFile(
        string path,
        bool isImage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AgentLocalPathGuard.ValidateExistingFile(path, "Agent evidence file");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = stream.Length;
        if (length is <= 0 or > MaxEvidenceBytes)
        {
            throw new InvalidOperationException(
                $"Evidence files must be between 1 byte and {MaxEvidenceBytes / (1024 * 1024):N0} MB.");
        }

        byte[] hash = SHA256.HashData(stream);
        if (!isImage)
        {
            return (Convert.ToHexString(hash).ToLowerInvariant(), null, null, length);
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.Position = 0;
        using SKCodec? codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            throw new InvalidOperationException("The selected image is corrupt or unsupported.");
        }

        long pixels = checked((long)codec.Info.Width * codec.Info.Height);
        if (codec.Info.Width > 20_000 || codec.Info.Height > 20_000 || pixels > 64_000_000)
        {
            throw new InvalidOperationException("The selected image is too large to inspect safely.");
        }

        return (Convert.ToHexString(hash).ToLowerInvariant(), codec.Info.Width, codec.Info.Height, length);
    }

    private static async Task<StableTextInspection> InspectTextFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string full = AgentLocalPathGuard.ValidateExistingFile(path, "Agent text evidence file");
        await using var stream = new FileStream(
            full,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = stream.Length;
        if (length is <= 0 or > 2L * 1024 * 1024)
        {
            throw new InvalidOperationException("Reviewed text evidence must be between 1 byte and 2 MB.");
        }

        byte[] bytes = new byte[checked((int)length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var memory = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(memory, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (text.Contains('\0'))
        {
            throw new InvalidOperationException("The selected file appears to be binary, not text.");
        }

        if (text.Length > AiTextActionLimits.MaxInputCharacters)
        {
            throw new InvalidOperationException(
                $"Agent Workspace accepts up to {AiTextActionLimits.MaxInputCharacters:N0} text characters per file.");
        }

        return new StableTextInspection(text, sha256, length);
    }

    private sealed record StableTextInspection(string Text, string Sha256, long Length);

    private void DeleteOwnedTemporaryFile(string path)
    {
        try
        {
            string tempRoot = AgentLocalPathGuard.ValidateExistingDirectory(
                _paths.TempExportsDirectory,
                "Agent Workspace temporary storage");
            string full = AgentLocalPathGuard.ValidateExistingFile(path, "Owned temporary evidence");
            string relative = Path.GetRelativePath(tempRoot, full);
            if (!Path.IsPathRooted(relative) &&
                relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                File.Exists(full))
            {
                File.Delete(full);
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
        }
    }

    private static string SafeExtension(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length is < 2 or > 12 || normalized[0] != '.' ||
            normalized.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            return ".bin";
        }

        return normalized;
    }

    private static string? MediaType(string extension, bool isImage) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        ".txt" or ".log" => "text/plain",
        ".md" or ".markdown" => "text/markdown",
        ".json" => "application/json",
        ".csv" => "text/csv",
        ".xml" => "application/xml",
        ".yaml" or ".yml" => "application/yaml",
        _ when isImage => "application/octet-stream",
        _ => null,
    };
}

public sealed record AgentPacketBundle(
    string DirectoryPath,
    string TaskPath,
    string ManifestPath,
    IReadOnlyList<string> ImagePaths);

public interface IAgentPacketExportService
{
    Task<AgentPacketBundle> ExportAsync(
        ReviewedAgentPacket packet,
        IReadOnlyList<AgentWorkspaceEvidence> evidence,
        string destinationParent,
        CancellationToken cancellationToken = default);
}

/// <summary>Materializes a reviewed packet as a self-contained, collision-safe folder.</summary>
public sealed class AgentPacketExportService : IAgentPacketExportService
{
    private readonly ISafeFileWriter _safeWriter;

    public AgentPacketExportService(ISafeFileWriter safeWriter)
        => _safeWriter = safeWriter ?? throw new ArgumentNullException(nameof(safeWriter));

    public async Task<AgentPacketBundle> ExportAsync(
        ReviewedAgentPacket packet,
        IReadOnlyList<AgentWorkspaceEvidence> evidence,
        string destinationParent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(evidence);
        if (string.IsNullOrWhiteSpace(destinationParent))
        {
            throw new InvalidOperationException("Choose a local folder for the Agent Packet.");
        }

        string parent = AgentLocalPathGuard.ValidateDestinationDirectory(
            destinationParent,
            "Agent Packet destination");
        Directory.CreateDirectory(parent);
        AgentLocalPathGuard.ValidateExistingDirectory(parent, "Agent Packet destination");
        List<AgentWorkspaceEvidence> attached = evidence
            .Where(item => item.IsIncluded && item.RelativeAssetPath is not null)
            .ToList();
        long attachmentBytes = attached.Sum(item => item.SizeBytes);
        if (attachmentBytes > AgentEvidenceFactory.MaxPacketAttachmentBytes)
        {
            throw new InvalidOperationException(
                $"Agent Packet attachments exceed the {AgentEvidenceFactory.MaxPacketAttachmentBytes / (1024 * 1024):N0} MB aggregate limit.");
        }

        string driveRoot = Path.GetPathRoot(parent)!;
        var drive = new DriveInfo(driveRoot);
        long documentBytes = checked((long)Encoding.UTF8.GetByteCount(packet.OutboundMarkdown) +
            Encoding.UTF8.GetByteCount(packet.ManifestJson));
        long requiredBytes = checked(attachmentBytes + documentBytes + (16L * 1024 * 1024));
        if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException(
                $"The destination needs at least {FormatBytes(requiredBytes)} free for this Agent Packet.");
        }

        string finalRoot = UniqueDirectory(parent, $"octadock-agent-{packet.Metadata.Id}");
        string stagingRoot = Path.Combine(parent, $".octadock-agent-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);
        try
        {
            var byRelativePath = attached
                .ToDictionary(item => item.RelativeAssetPath!, StringComparer.Ordinal);
            var materializedHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (ReviewedAgentPacketSource source in packet.Sources)
            {
                foreach (ReviewedAgentPacketAsset asset in source.Assets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!byRelativePath.TryGetValue(asset.RelativePath, out AgentWorkspaceEvidence? item) ||
                        string.IsNullOrWhiteSpace(item.LocalPath))
                    {
                        throw new InvalidOperationException($"Reviewed asset '{asset.RelativePath}' no longer has a local source.");
                    }

                    string target = ResolveInside(stagingRoot, asset.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    string actual = await CopyReviewedAssetAsync(item, target, cancellationToken)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(asset.Sha256) &&
                        !string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Evidence '{item.Label}' changed after review. Review the packet again.");
                    }

                    materializedHashes[asset.RelativePath] = actual;
                }
            }

            string taskPath = Path.Combine(stagingRoot, "TASK.md");
            string manifestPath = Path.Combine(stagingRoot, "manifest.json");
            await _safeWriter.WriteAsync(taskPath, Encoding.UTF8.GetBytes(packet.OutboundMarkdown), cancellationToken)
                .ConfigureAwait(false);
            await _safeWriter.WriteAsync(manifestPath, Encoding.UTF8.GetBytes(packet.ManifestJson), cancellationToken)
                .ConfigureAwait(false);
            var checksums = new StringBuilder();
            checksums.Append(packet.OutboundSha256).Append("  TASK.md\n");
            checksums.Append(packet.ManifestSha256).Append("  manifest.json\n");
            foreach ((string relativePath, string hash) in materializedHashes)
            {
                checksums.Append(hash).Append("  ").Append(relativePath).Append('\n');
            }

            await _safeWriter.WriteAsync(
                    Path.Combine(stagingRoot, "SHA256SUMS"),
                    Encoding.UTF8.GetBytes(checksums.ToString()),
                    cancellationToken)
                .ConfigureAwait(false);
            Directory.Move(stagingRoot, finalRoot);

            IReadOnlyList<string> images = packet.Sources
                .SelectMany(source => source.Assets)
                .Where(asset => asset.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
                .Select(asset => ResolveInside(finalRoot, asset.RelativePath))
                .ToList();
            return new AgentPacketBundle(
                finalRoot,
                Path.Combine(finalRoot, "TASK.md"),
                Path.Combine(finalRoot, "manifest.json"),
                images);
        }
        catch
        {
            DeleteStagingInside(parent, stagingRoot);
            throw;
        }
    }

    private static string UniqueDirectory(string parent, string stem)
    {
        for (int index = 1; ; index++)
        {
            string candidate = Path.Combine(parent, index == 1 ? stem : $"{stem}-{index}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private async Task<string> CopyReviewedAssetAsync(
        AgentWorkspaceEvidence item,
        string destination,
        CancellationToken cancellationToken)
    {
        string source = AgentLocalPathGuard.ValidateExistingFile(
            item.LocalPath!,
            "Reviewed Agent Packet asset");
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != item.SizeBytes || input.Length > AgentEvidenceFactory.MaxEvidenceBytes)
        {
            throw new InvalidOperationException($"Evidence '{item.Label}' changed after review. Review the packet again.");
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long copied = 0;
        await _safeWriter.WriteAsync(
            destination,
            async (output, token) =>
            {
                byte[] buffer = new byte[64 * 1024];
                while (true)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    copied = checked(copied + read);
                    if (copied > item.SizeBytes || copied > AgentEvidenceFactory.MaxEvidenceBytes)
                    {
                        throw new InvalidOperationException(
                            $"Evidence '{item.Label}' exceeded its reviewed byte limit while being copied.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
        if (copied != item.SizeBytes)
        {
            throw new InvalidOperationException($"Evidence '{item.Label}' changed after review. Review the packet again.");
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string FormatBytes(long bytes)
    {
        double value = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB"];
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return value.ToString(index == 0 ? "0" : "0.#", CultureInfo.CurrentCulture) + " " + units[index];
    }

    private static string ResolveInside(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Agent Packet asset paths must be relative.");
        }

        string fullRoot = Path.GetFullPath(root);
        string target = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string relative = Path.GetRelativePath(fullRoot, target);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An Agent Packet asset path escaped the bundle.");
        }

        return target;
    }

    private static void DeleteStagingInside(string parent, string stagingRoot)
    {
        try
        {
            string parentFull = Path.GetFullPath(parent);
            string stagingFull = Path.GetFullPath(stagingRoot);
            string relative = Path.GetRelativePath(parentFull, stagingFull);
            if (relative.StartsWith(".octadock-agent-stage-", StringComparison.Ordinal) && Directory.Exists(stagingFull))
            {
                Directory.Delete(stagingFull, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public interface IAgentWorkspacePicker
{
    IReadOnlyList<string> PickEvidenceFiles();

    string? PickImage(string title);

    string? PickExportFolder();
}

public sealed class WpfAgentWorkspacePicker : IAgentWorkspacePicker
{
    public IReadOnlyList<string> PickEvidenceFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add evidence to handoff review",
            CheckFileExists = true,
            Multiselect = true,
            Filter = "Evidence|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.txt;*.md;*.markdown;*.json;*.csv;*.log;*.xml;*.yaml;*.yml;*.cs;*.ts;*.tsx;*.js;*.jsx;*.py;*.sql;*.html;*.css;*.pdf|All files|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    public string? PickImage(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            CheckFileExists = true,
            Multiselect = false,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickExportFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where to save the Agent Packet",
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}

public sealed record AgentHandoffReview(
    AiCliProviderDescriptor Provider,
    ReviewedAgentPacket Packet,
    int ArtifactCount,
    long ArtifactBytes,
    bool IncludesUnredactedPixels,
    string PacketDirectory);

public interface IAgentTemporaryLeaseStore
{
    string CreateLease(string path);

    bool TryClaim(string token, string path);

    void Release(string token);
}

/// <summary>
/// Short-lived ownership transfer for composed pin images. If Agent Workspace
/// never claims a handoff (busy, cancelled, superseded, or closed), the lease
/// deletes the managed temp automatically.
/// </summary>
public sealed class AgentTemporaryLeaseStore : IAgentTemporaryLeaseStore, IDisposable
{
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromMinutes(2);
    private readonly IStoragePaths _paths;
    private readonly ConcurrentDictionary<string, Lease> _leases = new(StringComparer.Ordinal);

    public AgentTemporaryLeaseStore(IStoragePaths paths)
        => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public string CreateLease(string path)
    {
        string full = ValidateOwnedPinPath(path);
        string token = Guid.NewGuid().ToString("N");
        var timer = new Timer(_ => Release(token), null, LeaseLifetime, Timeout.InfiniteTimeSpan);
        if (!_leases.TryAdd(token, new Lease(full, timer)))
        {
            timer.Dispose();
            throw new InvalidOperationException("Could not create the temporary evidence lease.");
        }

        return token;
    }

    public bool TryClaim(string token, string path)
    {
        if (string.IsNullOrWhiteSpace(token) || !_leases.TryGetValue(token, out Lease? lease))
        {
            return false;
        }

        string full;
        try
        {
            full = ValidateOwnedPinPath(path);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }

        if (!string.Equals(full, lease.Path, StringComparison.OrdinalIgnoreCase) ||
            !_leases.TryRemove(token, out Lease? claimed))
        {
            return false;
        }

        claimed.Timer.Dispose();
        return true;
    }

    public void Release(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !_leases.TryRemove(token, out Lease? lease))
        {
            return;
        }

        lease.Timer.Dispose();
        TryDeleteOwnedPin(lease.Path);
    }

    private string ValidateOwnedPinPath(string path)
    {
        string full = AgentLocalPathGuard.ValidateExistingFile(path, "Composed pin evidence");
        string root = AgentLocalPathGuard.ValidateExistingDirectory(
            _paths.TempExportsDirectory,
            "Pin temporary storage");
        string relative = Path.GetRelativePath(root, full);
        if (Path.IsPathRooted(relative) ||
            relative.Contains(Path.DirectorySeparatorChar) ||
            relative.Contains(Path.AltDirectorySeparatorChar) ||
            !Path.GetFileName(relative).StartsWith("pin-", StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(relative), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only an Octadock-managed composed pin can be leased.");
        }

        return full;
    }

    private void TryDeleteOwnedPin(string path)
    {
        try
        {
            // Revalidate at cleanup time. A lease is deletion authority for the
            // exact local managed pin only; it is not authority to follow a root
            // that has since become a junction, symlink, or mapped drive.
            string full = ValidateOwnedPinPath(path);
            File.Delete(full);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
        }
    }

    public void Dispose()
    {
        foreach (string token in _leases.Keys)
        {
            Release(token);
        }
    }

    private sealed record Lease(string Path, Timer Timer);
}

public interface IAgentHandoffConfirmation
{
    bool Confirm(AgentHandoffReview review);
}

public sealed class WpfAgentHandoffConfirmation : IAgentHandoffConfirmation
{
    public bool Confirm(AgentHandoffReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        string secretLine = review.Packet.DetectedSecretCount switch
        {
            0 => "No common secret patterns were detected in included text.\n",
            _ when review.Packet.TextSecretsRedacted =>
                $"{review.Packet.DetectedSecretCount:N0} common secret pattern(s) were redacted from included text.\n",
            _ =>
                $"Warning: {review.Packet.DetectedSecretCount:N0} common secret pattern(s) remain visible in included text.\n",
        };
        string attachmentLine = review.ArtifactCount > 0
            ? "Important: attached images and binary files are included unchanged; text redaction does not alter or scan their contents.\n"
            : string.Empty;
        string message =
            $"Run a read-only analysis with {review.Provider.DisplayName}?\n\n" +
            $"{review.ArtifactCount:N0} attachment(s), {FormatBytes(review.ArtifactBytes)}, and " +
            $"{review.Packet.OutboundCharacterCount:N0} reviewed text characters will be available to " +
            $"{review.Provider.DestinationDisclosure}.\n\n" +
            secretLine +
            attachmentLine +
            "The agent may read this packet but cannot edit your files. Octadock will not fall back to another provider.";
        bool needsWarning =
            review.IncludesUnredactedPixels ||
            review.ArtifactCount > 0 ||
            (review.Packet.DetectedSecretCount > 0 && !review.Packet.TextSecretsRedacted);
        return ConfirmationDialog.Ask(
            owner: null,
            title: $"Analyze with {review.Provider.DisplayName}",
            message: message,
            primaryText: "Run read-only analysis",
            cancelText: "Don’t run",
            tone: needsWarning ? ConfirmationDialogTone.Warning : ConfirmationDialogTone.Neutral);
    }

    private static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB"];
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {units[index]}");
    }
}
