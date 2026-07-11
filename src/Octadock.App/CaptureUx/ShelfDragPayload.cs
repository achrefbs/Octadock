using System.Windows;

namespace Octadock.App.CaptureUx;

/// <summary>
/// Private drag format that lets the Shelf distinguish its own capture from an
/// unrelated image file. The normal FileDrop and Bitmap formats remain present
/// for Explorer, browsers, chat applications, and image editors.
/// </summary>
internal static class ShelfDragPayload
{
    internal const string CaptureIdFormat = "Octadock.CaptureId";

    internal static void SetCaptureId(DataObject data, Guid captureId)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.SetData(CaptureIdFormat, captureId.ToString("D"));
    }

    internal static bool TryGetCaptureId(IDataObject data, out Guid captureId)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            if (data.GetDataPresent(CaptureIdFormat))
            {
                return TryParseCaptureId(data.GetData(CaptureIdFormat), out captureId);
            }
        }
        catch (Exception)
        {
            // A malformed third-party/COM data object must not break drag/drop.
        }

        captureId = Guid.Empty;
        return false;
    }

    internal static bool TryParseCaptureId(object? value, out Guid captureId)
    {
        if (value is Guid guid && guid != Guid.Empty)
        {
            captureId = guid;
            return true;
        }

        if (value is string text && Guid.TryParse(text, out guid) && guid != Guid.Empty)
        {
            captureId = guid;
            return true;
        }

        captureId = Guid.Empty;
        return false;
    }
}
