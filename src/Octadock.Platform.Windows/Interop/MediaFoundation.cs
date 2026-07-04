using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Octadock.Platform.Windows.Interop;

/// <summary>
/// P/Invoke and COM interop for a minimal Media Foundation H.264/MP4 sink-writer
/// pipeline. Declares only the functions and interface methods the recording
/// engine needs. Runtime validation requires an encoder MFT and writable output
/// path, so tests cover the pure helpers while the app keeps failure paths soft.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class MediaFoundation
{
    private const string Mfplat = "mfplat.dll";
    private const string Mfreadwrite = "mfreadwrite.dll";

    /// <summary>MF version for <c>MFStartup</c> (<c>MF_VERSION</c>, 0x00020070).</summary>
    public const uint MF_VERSION = 0x00020070;

    /// <summary>Do not use the socket layer (<c>MFSTARTUP_LITE</c>).</summary>
    public const uint MFSTARTUP_LITE = 1;

    [LibraryImport(Mfplat)]
    public static partial int MFStartup(uint version, uint dwFlags);

    [LibraryImport(Mfplat)]
    public static partial int MFShutdown();

    [LibraryImport(Mfplat)]
    public static partial int MFCreateMediaType(out nint ppMFType);

    [LibraryImport(Mfplat)]
    public static partial int MFCreateSample(out nint ppIMFSample);

    [LibraryImport(Mfplat)]
    public static partial int MFCreateMemoryBuffer(uint cbMaxLength, out nint ppBuffer);

    [LibraryImport(Mfplat)]
    public static partial int MFCreateAttributes(out nint ppMFAttributes, uint cInitialSize);

    /// <summary>
    /// Creates a sink writer that encodes to the given URL. Pass a container-type
    /// attribute store (or <c>nint.Zero</c> to infer from the extension).
    /// </summary>
    [DllImport(Mfreadwrite, CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int MFCreateSinkWriterFromURL(
        string pwszOutputURL,
        nint pByteStream,
        nint pAttributes,
        out nint ppSinkWriter);

    /// <summary>The first (only) output/input stream index used by the engine.</summary>
    public const uint FirstStreamIndex = 0;
}

/// <summary><c>IMFSinkWriter</c> — writes encoded samples to a media sink.</summary>
[ComImport]
[Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[SupportedOSPlatform("windows")]
internal interface IMFSinkWriter
{
    [PreserveSig]
    int AddStream(nint pTargetMediaType, out uint pdwStreamIndex);

    [PreserveSig]
    int SetInputMediaType(uint dwStreamIndex, nint pInputMediaType, nint pEncodingParameters);

    [PreserveSig]
    int BeginWriting();

    [PreserveSig]
    int WriteSample(uint dwStreamIndex, nint pSample);

    [PreserveSig]
    int SendStreamTick(uint dwStreamIndex, long llTimestamp);

    [PreserveSig]
    int PlaceMarker(uint dwStreamIndex, nint pvContext);

    [PreserveSig]
    int NotifyEndOfSegment(uint dwStreamIndex);

    [PreserveSig]
    int Flush(uint dwStreamIndex);

    [PreserveSig]
    int Finalize_();

    [PreserveSig]
    int GetServiceForStream(uint dwStreamIndex, ref Guid guidService, ref Guid riid, out nint ppvObject);

    [PreserveSig]
    int GetStatistics(uint dwStreamIndex, nint pStats);
}

/// <summary><c>IMFMediaType</c> — media format description (subset).</summary>
[ComImport]
[Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[SupportedOSPlatform("windows")]
internal interface IMFMediaType
{
    // IMFAttributes (subset used here). GetItem is slot 3; only the setters we use
    // are declared with real signatures — earlier/unused slots keep vtable order.
    [PreserveSig]
    int GetItem(ref Guid guidKey, nint pValue);

    [PreserveSig]
    int GetItemType(ref Guid guidKey, out int pType);

    [PreserveSig]
    int CompareItem(ref Guid guidKey, nint value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);

    [PreserveSig]
    int Compare(nint pTheirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);

    [PreserveSig]
    int GetUINT32(ref Guid guidKey, out uint punValue);

    [PreserveSig]
    int GetUINT64(ref Guid guidKey, out ulong punValue);

    [PreserveSig]
    int GetDouble(ref Guid guidKey, out double pfValue);

    [PreserveSig]
    int GetGUID(ref Guid guidKey, out Guid pguidValue);

    [PreserveSig]
    int GetStringLength(ref Guid guidKey, out uint pcchLength);

    [PreserveSig]
    int GetString(ref Guid guidKey, nint pwszValue, uint cchBufSize, nint pcchLength);

    [PreserveSig]
    int GetAllocatedString(ref Guid guidKey, out nint ppwszValue, out uint pcchLength);

    [PreserveSig]
    int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);

    [PreserveSig]
    int GetBlob(ref Guid guidKey, nint pBuf, uint cbBufSize, nint pcbBlobSize);

    [PreserveSig]
    int GetAllocatedBlob(ref Guid guidKey, out nint ppBuf, out uint pcbSize);

    [PreserveSig]
    int GetUnknown(ref Guid guidKey, ref Guid riid, out nint ppv);

    [PreserveSig]
    int SetItem(ref Guid guidKey, nint value);

    [PreserveSig]
    int DeleteItem(ref Guid guidKey);

    [PreserveSig]
    int DeleteAllItems();

    [PreserveSig]
    int SetUINT32(ref Guid guidKey, uint unValue);

    [PreserveSig]
    int SetUINT64(ref Guid guidKey, ulong unValue);

    [PreserveSig]
    int SetDouble(ref Guid guidKey, double fValue);

    [PreserveSig]
    int SetGUID(ref Guid guidKey, ref Guid guidValue);

    [PreserveSig]
    int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);

    [PreserveSig]
    int SetBlob(ref Guid guidKey, nint pBuf, uint cbBufSize);

    [PreserveSig]
    int SetUnknown(ref Guid guidKey, nint pUnknown);

    [PreserveSig]
    int LockStore();

    [PreserveSig]
    int UnlockStore();

    [PreserveSig]
    int GetCount(out uint pcItems);

    [PreserveSig]
    int GetItemByIndex(uint unIndex, out Guid pguidKey, nint pValue);

    [PreserveSig]
    int CopyAllItems(nint pDest);

    // IMFMediaType-specific members follow (not needed here).
}

/// <summary><c>IMFSample</c> — a sample carrying media buffers and timing (subset).</summary>
[ComImport]
[Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[SupportedOSPlatform("windows")]
internal interface IMFSample
{
    // IMFAttributes vtable precedes IMFSample methods; declare opaque placeholders
    // in exact order (33 IMFAttributes methods) so the sample methods land right.
    [PreserveSig] int GetItem(ref Guid k, nint v);
    [PreserveSig] int GetItemType(ref Guid k, out int t);
    [PreserveSig] int CompareItem(ref Guid k, nint v, [MarshalAs(UnmanagedType.Bool)] out bool r);
    [PreserveSig] int Compare(nint p, int m, [MarshalAs(UnmanagedType.Bool)] out bool r);
    [PreserveSig] int GetUINT32(ref Guid k, out uint v);
    [PreserveSig] int GetUINT64(ref Guid k, out ulong v);
    [PreserveSig] int GetDouble(ref Guid k, out double v);
    [PreserveSig] int GetGUID(ref Guid k, out Guid v);
    [PreserveSig] int GetStringLength(ref Guid k, out uint l);
    [PreserveSig] int GetString(ref Guid k, nint v, uint c, nint pl);
    [PreserveSig] int GetAllocatedString(ref Guid k, out nint v, out uint l);
    [PreserveSig] int GetBlobSize(ref Guid k, out uint s);
    [PreserveSig] int GetBlob(ref Guid k, nint b, uint c, nint ps);
    [PreserveSig] int GetAllocatedBlob(ref Guid k, out nint b, out uint s);
    [PreserveSig] int GetUnknown(ref Guid k, ref Guid iid, out nint ppv);
    [PreserveSig] int SetItem(ref Guid k, nint v);
    [PreserveSig] int DeleteItem(ref Guid k);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(ref Guid k, uint v);
    [PreserveSig] int SetUINT64(ref Guid k, ulong v);
    [PreserveSig] int SetDouble(ref Guid k, double v);
    [PreserveSig] int SetGUID(ref Guid k, ref Guid v);
    [PreserveSig] int SetString(ref Guid k, [MarshalAs(UnmanagedType.LPWStr)] string v);
    [PreserveSig] int SetBlob(ref Guid k, nint b, uint c);
    [PreserveSig] int SetUnknown(ref Guid k, nint u);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint c);
    [PreserveSig] int GetItemByIndex(uint i, out Guid k, nint v);
    [PreserveSig] int CopyAllItems(nint dest);

    // IMFSample members.
    [PreserveSig] int GetSampleFlags(out uint pdwSampleFlags);
    [PreserveSig] int SetSampleFlags(uint dwSampleFlags);
    [PreserveSig] int GetSampleTime(out long phnsSampleTime);
    [PreserveSig] int SetSampleTime(long hnsSampleTime);
    [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
    [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
    [PreserveSig] int GetBufferCount(out uint pdwBufferCount);
    [PreserveSig] int GetBufferByIndex(uint dwIndex, out nint ppBuffer);
    [PreserveSig] int ConvertToContiguousBuffer(out nint ppBuffer);
    [PreserveSig] int AddBuffer(nint pBuffer);
    [PreserveSig] int RemoveBufferByIndex(uint dwIndex);
    [PreserveSig] int RemoveAllBuffers();
    [PreserveSig] int GetTotalLength(out uint pcbTotalLength);
    [PreserveSig] int CopyToBuffer(nint pBuffer);
}

/// <summary><c>IMFMediaBuffer</c> — a lockable media memory buffer.</summary>
[ComImport]
[Guid("045FA593-8799-42b8-BC8D-8968C6453507")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[SupportedOSPlatform("windows")]
internal interface IMFMediaBuffer
{
    [PreserveSig]
    int Lock(out nint ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);

    [PreserveSig]
    int Unlock();

    [PreserveSig]
    int GetCurrentLength(out uint pcbCurrentLength);

    [PreserveSig]
    int SetCurrentLength(uint cbCurrentLength);

    [PreserveSig]
    int GetMaxLength(out uint pcbMaxLength);
}

/// <summary>
/// <c>IMFAttributes</c> — a bare attribute store (used for sink-writer creation
/// attributes). Same 30-method layout as the attribute prefix of
/// <see cref="IMFMediaType"/>, but with the store's own IID so the cast succeeds.
/// </summary>
[ComImport]
[Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[SupportedOSPlatform("windows")]
internal interface IMFAttributes
{
    [PreserveSig] int GetItem(ref Guid guidKey, nint pValue);
    [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
    [PreserveSig] int CompareItem(ref Guid guidKey, nint value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
    [PreserveSig] int Compare(nint pTheirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
    [PreserveSig] int GetUINT32(ref Guid guidKey, out uint punValue);
    [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong punValue);
    [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
    [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
    [PreserveSig] int GetStringLength(ref Guid guidKey, out uint pcchLength);
    [PreserveSig] int GetString(ref Guid guidKey, nint pwszValue, uint cchBufSize, nint pcchLength);
    [PreserveSig] int GetAllocatedString(ref Guid guidKey, out nint ppwszValue, out uint pcchLength);
    [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
    [PreserveSig] int GetBlob(ref Guid guidKey, nint pBuf, uint cbBufSize, nint pcbBlobSize);
    [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out nint ppBuf, out uint pcbSize);
    [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out nint ppv);
    [PreserveSig] int SetItem(ref Guid guidKey, nint value);
    [PreserveSig] int DeleteItem(ref Guid guidKey);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(ref Guid guidKey, uint unValue);
    [PreserveSig] int SetUINT64(ref Guid guidKey, ulong unValue);
    [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
    [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
    [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
    [PreserveSig] int SetBlob(ref Guid guidKey, nint pBuf, uint cbBufSize);
    [PreserveSig] int SetUnknown(ref Guid guidKey, nint pUnknown);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint pcItems);
    [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, nint pValue);
    [PreserveSig] int CopyAllItems(nint pDest);
}

/// <summary>Media Foundation attribute GUIDs and media subtype identifiers.</summary>
[SupportedOSPlatform("windows")]
internal static class MfGuids
{
    // Major/subtype and format attribute keys.
    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9c-bd0f-6d873c78e9d5");
    public static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");

    // Major types.
    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");

    // Video subtypes.
    public static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_ARGB32 = new("00000021-0000-0010-8000-00aa00389b71");

    // Interlace mode value: progressive.
    public const uint MFVideoInterlace_Progressive = 2;

    // IMFMediaType IID (for GetServiceForStream / QueryInterface if needed).
    public static readonly Guid IID_IMFMediaType = new("44ae0fa8-ea31-4109-8d2e-4cae4997c555");

    /// <summary>
    /// <c>MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS</c>. Without it the sink
    /// writer only considers software MFTs; on machines whose registered H.264
    /// encoder is hardware (NVENC/AMF/QuickSync) the RGB32 input negotiation can
    /// fail with <c>MF_E_INVALIDMEDIATYPE</c> because no converter chain forms.
    /// </summary>
    public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
}
