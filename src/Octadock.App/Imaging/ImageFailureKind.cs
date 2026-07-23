namespace Octadock.App.Imaging;

/// <summary>
/// Stable failure categories for image decode/load, shared by the capture,
/// thumbnail, clipboard and annotation paths. Raw exception messages never
/// reach the user; <see cref="ImageFailureCopy"/> owns the product copy.
/// </summary>
internal enum ImageFailureKind
{
    None = 0,
    NotFound,
    AccessDenied,
    Busy,
    Malformed,
    TooLarge,
    CodecUnavailable,
    Empty,
    Unknown,
}

/// <summary>Canonical product copy for image load failures.</summary>
internal static class ImageFailureCopy
{
    public static string For(ImageFailureKind kind) => kind switch
    {
        ImageFailureKind.NotFound => "This image is no longer at that location.",
        ImageFailureKind.AccessDenied => "Octadock does not have permission to read this image.",
        ImageFailureKind.Busy => "This image is in use by another app. Close it or try again.",
        ImageFailureKind.Malformed => "This image is damaged or is not valid for its file type.",
        ImageFailureKind.TooLarge => "This image exceeds Octadock's safe decode limits.",
        ImageFailureKind.CodecUnavailable => "Windows does not have the codec needed to open this image.",
        ImageFailureKind.Empty => "0 bytes — nothing to show.",
        _ => "Octadock could not decode this image.",
    };
}
