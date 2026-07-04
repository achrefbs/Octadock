namespace Octadock.Platform.Windows.Interop;

/// <summary>
/// Win32 constants used across the platform layer. Grouped here so the
/// individual <c>NativeMethods</c> partials stay focused on signatures.
/// </summary>
internal static class NativeConstants
{
    // Window messages.
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_APP = 0x8000;

    /// <summary>Posted to the hotkey thread to request an orderly shutdown of its pump.</summary>
    public const uint WM_OCTADOCK_QUIT = WM_APP + 1;

    // RegisterHotKey modifier flags. Match Octadock.Core.Hotkeys.ModifierKeys where possible.
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // Extended window styles.
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_TRANSPARENT = 0x00000020;
    public const long WS_EX_APPWINDOW = 0x00040000;

    // Window display affinity (SetWindowDisplayAffinity).
    public const uint WDA_NONE = 0x00000000;
    public const uint WDA_MONITOR = 0x00000001;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    // MonitorFrom* flags.
    public const uint MONITOR_DEFAULTTONULL = 0x00000000;
    public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    // MONITORINFO flags.
    public const uint MONITORINFOF_PRIMARY = 0x00000001;

    // GDI BitBlt raster-operation codes.
    public const uint SRCCOPY = 0x00CC0020;
    public const uint CAPTUREBLT = 0x40000000;

    // DIB color usage.
    public const uint DIB_RGB_COLORS = 0;

    // BITMAPINFOHEADER compression.
    public const uint BI_RGB = 0;

    // Message-only window parent sentinel.
    public static readonly nint HWND_MESSAGE = -3;

    // GetWindow / process access.
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // PrintWindow flags.
    public const uint PW_CLIENTONLY = 0x00000001;
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    // DrawIconEx flags.
    public const uint DI_NORMAL = 0x0003;

    // CURSORINFO flags.
    public const int CURSOR_SHOWING = 0x00000001;

    // GetLastError values of interest.
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    // GetMessage return sentinel.
    public const int WM_QUIT = 0x0012;
}

/// <summary>DWM window-attribute identifiers passed to <c>DwmGetWindowAttribute</c>.</summary>
internal enum DWMWINDOWATTRIBUTE
{
    DWMWA_EXTENDED_FRAME_BOUNDS = 9,
    DWMWA_CLOAKED = 14,
}

/// <summary>Monitor DPI type for <c>GetDpiForMonitor</c>.</summary>
internal enum MONITOR_DPI_TYPE
{
    MDT_EFFECTIVE_DPI = 0,
    MDT_ANGULAR_DPI = 1,
    MDT_RAW_DPI = 2,
    MDT_DEFAULT = MDT_EFFECTIVE_DPI,
}

/// <summary>Scale factor values returned by <c>GetScaleFactorForMonitor</c>.</summary>
internal enum DEVICE_SCALE_FACTOR
{
    DEVICE_SCALE_FACTOR_INVALID = 0,
    SCALE_100_PERCENT = 100,
    SCALE_125_PERCENT = 125,
    SCALE_150_PERCENT = 150,
    SCALE_175_PERCENT = 175,
    SCALE_200_PERCENT = 200,
    SCALE_225_PERCENT = 225,
    SCALE_250_PERCENT = 250,
    SCALE_300_PERCENT = 300,
    SCALE_350_PERCENT = 350,
    SCALE_400_PERCENT = 400,
    SCALE_450_PERCENT = 450,
    SCALE_500_PERCENT = 500,
}
