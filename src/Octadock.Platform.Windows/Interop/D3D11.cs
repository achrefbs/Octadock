using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Octadock.Platform.Windows.Interop;

/// <summary>D3D11 driver types (see <c>D3D_DRIVER_TYPE</c>).</summary>
internal enum D3D_DRIVER_TYPE
{
    Unknown = 0,
    Hardware = 1,
    Reference = 2,
    Null = 3,
    Software = 4,
    Warp = 5,
}

/// <summary>Subset of <c>DXGI_FORMAT</c> values used by the capture path.</summary>
internal enum DXGI_FORMAT : uint
{
    Unknown = 0,
    R8G8B8A8_UNORM = 28,
    B8G8R8A8_UNORM = 87,
}

/// <summary>D3D11 usage flags (see <c>D3D11_USAGE</c>).</summary>
internal enum D3D11_USAGE
{
    Default = 0,
    Immutable = 1,
    Dynamic = 2,
    Staging = 3,
}

/// <summary>D3D11 CPU access flags.</summary>
[Flags]
internal enum D3D11_CPU_ACCESS_FLAG
{
    None = 0,
    Write = 0x10000,
    Read = 0x20000,
}

/// <summary>D3D11 map types (see <c>D3D11_MAP</c>).</summary>
internal enum D3D11_MAP
{
    Read = 1,
    Write = 2,
    ReadWrite = 3,
    WriteDiscard = 4,
    WriteNoOverwrite = 5,
}

/// <summary>The <c>D3D11_TEXTURE2D_DESC</c> structure.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_TEXTURE2D_DESC
{
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public DXGI_FORMAT Format;
    public DXGI_SAMPLE_DESC SampleDesc;
    public D3D11_USAGE Usage;
    public uint BindFlags;
    public uint CPUAccessFlags;
    public uint MiscFlags;
}

/// <summary>The <c>DXGI_SAMPLE_DESC</c> structure.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_SAMPLE_DESC
{
    public uint Count;
    public uint Quality;
}

/// <summary>The <c>D3D11_MAPPED_SUBRESOURCE</c> structure returned by <c>Map</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_MAPPED_SUBRESOURCE
{
    public nint PData;
    public uint RowPitch;
    public uint DepthPitch;
}

/// <summary>P/Invoke for <c>d3d11.dll</c> device creation and WinRT interop bridging.</summary>
[SupportedOSPlatform("windows")]
internal static partial class D3D11
{
    private const string Dll = "d3d11.dll";

    /// <summary>
    /// Creates a D3D11 device. A hardware device is requested with BGRA support so
    /// it is compatible with Direct2D/DirectComposition and the WGC frame pool.
    /// </summary>
    [DllImport(Dll, ExactSpelling = true)]
    public static extern int D3D11CreateDevice(
        nint pAdapter,
        D3D_DRIVER_TYPE driverType,
        nint software,
        uint flags,
        nint pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out nint ppDevice,
        out uint pFeatureLevel,
        out nint ppImmediateContext);

    /// <summary>D3D11 SDK version constant (<c>D3D11_SDK_VERSION</c>).</summary>
    public const uint D3D11_SDK_VERSION = 7;

    /// <summary><c>D3D11_CREATE_DEVICE_BGRA_SUPPORT</c>.</summary>
    public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
}
