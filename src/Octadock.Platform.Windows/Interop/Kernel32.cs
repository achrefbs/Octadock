using System.Runtime.InteropServices;

namespace Octadock.Platform.Windows.Interop;

/// <summary>P/Invoke declarations for <c>kernel32.dll</c>.</summary>
internal static partial class Kernel32
{
    private const string Dll = "kernel32.dll";

    [LibraryImport(Dll, SetLastError = true)]
    public static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    /// <summary>
    /// Retrieves the full image path of a process. The path is written into
    /// <paramref name="lpExeName"/>; <paramref name="lpdwSize"/> is in/out (buffer
    /// capacity in, characters written out).
    /// </summary>
    // Classic DllImport: [Out] char[] with a caller-managed size buffer.
    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(nint hProcess, uint dwFlags, [Out] char[] lpExeName, ref uint lpdwSize);

    [LibraryImport(Dll, EntryPoint = "CreateMutexW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateMutex(nint lpMutexAttributes, [MarshalAs(UnmanagedType.Bool)] bool bInitialOwner, string lpName);

    [LibraryImport(Dll)]
    public static partial uint GetCurrentThreadId();

    [LibraryImport(Dll)]
    public static partial nint GetModuleHandleW(nint lpModuleName);

    [LibraryImport(Dll, SetLastError = true)]
    public static partial int GetLastError();
}
