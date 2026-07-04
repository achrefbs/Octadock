using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WinRT;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Octadock.Platform.Windows.Interop;

/// <summary>
/// COM interop shims that bridge Win32/D3D11 with the WinRT
/// Windows.Graphics.Capture projection: creating a capture item from an HWND or
/// HMONITOR, wrapping a D3D11 device as a WinRT <see cref="IDirect3DDevice"/>,
/// and reaching the native texture behind a captured surface.
/// </summary>
/// <remarks>
/// Uses the C#/WinRT interop surface (<c>T.As&lt;TInterop&gt;()</c>,
/// <c>MarshalInspectable&lt;T&gt;.FromAbi</c>, <c>MarshalInterface&lt;T&gt;.FromAbi</c>)
/// rather than the legacy <c>Marshal</c> RCW helpers, which are incompatible with
/// .NET ComWrappers. Runtime validation happens through the WGC fallback path.
/// </remarks>
[SupportedOSPlatform("windows10.0.19041.0")]
internal static class WinRtInterop
{
    // The projected activation IID for GraphicsCaptureItem.
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    // ID3D11Texture2D IID for reaching the native texture behind a surface.
    private static readonly Guid ID3D11Texture2DIid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    // IDXGIDevice IID for querying the D3D11 device for its DXGI face.
    private static readonly Guid IDXGIDeviceIid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    /// <summary>
    /// Bridges a native DXGI device pointer to a WinRT <see cref="IDirect3DDevice"/>.
    /// </summary>
    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    /// <summary>
    /// Wraps a native D3D11 device (from <see cref="D3D11.D3D11CreateDevice"/>) as a
    /// WinRT <see cref="IDirect3DDevice"/> suitable for the WGC frame pool.
    /// </summary>
    public static IDirect3DDevice CreateDirect3DDevice(nint d3dDevicePtr)
    {
        nint dxgiDevice = nint.Zero;
        nint graphicsDevicePtr = nint.Zero;
        try
        {
            Guid dxgiIid = IDXGIDeviceIid;
            int hr = Marshal.QueryInterface(d3dDevicePtr, ref dxgiIid, out dxgiDevice);
            Marshal.ThrowExceptionForHR(hr);

            hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out graphicsDevicePtr);
            Marshal.ThrowExceptionForHR(hr);

            // Materialize the projected WinRT object from the IInspectable pointer.
            // FromAbi calls AddRef, so we release our reference in the finally block.
            return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevicePtr);
        }
        finally
        {
            if (graphicsDevicePtr != nint.Zero)
            {
                Marshal.Release(graphicsDevicePtr);
            }

            if (dxgiDevice != nint.Zero)
            {
                Marshal.Release(dxgiDevice);
            }
        }
    }

    /// <summary>Creates a <see cref="GraphicsCaptureItem"/> for a top-level window.</summary>
    public static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
    {
        IGraphicsCaptureItemInterop interop = GetInterop();
        Guid iid = GraphicsCaptureItemIid;
        nint itemPtr = interop.CreateForWindow(hwnd, ref iid);
        try
        {
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            if (itemPtr != nint.Zero)
            {
                Marshal.Release(itemPtr);
            }
        }
    }

    /// <summary>Creates a <see cref="GraphicsCaptureItem"/> for a monitor (HMONITOR).</summary>
    public static GraphicsCaptureItem CreateItemForMonitor(nint hmon)
    {
        IGraphicsCaptureItemInterop interop = GetInterop();
        Guid iid = GraphicsCaptureItemIid;
        nint itemPtr = interop.CreateForMonitor(hmon, ref iid);
        try
        {
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            if (itemPtr != nint.Zero)
            {
                Marshal.Release(itemPtr);
            }
        }
    }

    /// <summary>
    /// Retrieves the native D3D11 texture (as an <c>ID3D11Texture2D</c> pointer)
    /// backing a WinRT <see cref="IDirect3DSurface"/>. Caller must
    /// <c>Marshal.Release</c> the returned pointer.
    /// </summary>
    public static nint GetDxgiTexture(IDirect3DSurface surface)
    {
        // .As<T>() casts a projected WinRT object to a user-defined ComImport
        // interop interface (C#/WinRT casting surface).
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        Guid iid = ID3D11Texture2DIid;
        return access.GetInterface(ref iid);
    }

    private static IGraphicsCaptureItemInterop GetInterop()
    {
        // T.As<TInterop>() returns the class factory's ComImport interop interface.
        return GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
    }
}

/// <summary>
/// The classic-COM interop interface exposed by the <see cref="GraphicsCaptureItem"/>
/// activation factory to create items from Win32 handles.
/// </summary>
[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    /// <summary>Creates a capture item for a window; returns an ABI pointer.</summary>
    nint CreateForWindow([In] nint window, [In] ref Guid iid);

    /// <summary>Creates a capture item for a monitor; returns an ABI pointer.</summary>
    nint CreateForMonitor([In] nint monitor, [In] ref Guid iid);
}

/// <summary>
/// <c>IDirect3DDxgiInterfaceAccess</c>: reaches the native DXGI interface behind a
/// WinRT Direct3D surface/device.
/// </summary>
[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    /// <summary>Returns the requested native interface pointer (caller releases it).</summary>
    nint GetInterface([In] ref Guid iid);
}
