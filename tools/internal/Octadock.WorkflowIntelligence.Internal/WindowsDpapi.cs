using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Octadock.WorkflowIntelligence.Internal;

internal static class WindowsDpapi
{
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("Octadock.WorkflowIntelligence.Internal.Key.v1");

    internal static byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        EnsureWindows();

        DataBlob input = Allocate(plaintext);
        DataBlob entropy = Allocate(Entropy);
        DataBlob output = default;
        try
        {
            if (!CryptProtectData(
                    ref input,
                    "Octadock internal workflow trace key",
                    ref entropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows DPAPI could not protect the trace key.");
            }

            return Copy(output);
        }
        finally
        {
            FreeAllocated(input);
            FreeAllocated(entropy);
            FreeLocal(output);
        }
    }

    internal static byte[] Unprotect(byte[] protectedBytes)
    {
        ArgumentNullException.ThrowIfNull(protectedBytes);
        EnsureWindows();

        DataBlob input = Allocate(protectedBytes);
        DataBlob entropy = Allocate(Entropy);
        DataBlob output = default;
        try
        {
            if (!CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    ref entropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows DPAPI could not unprotect the trace key.");
            }

            return Copy(output);
        }
        finally
        {
            FreeAllocated(input);
            FreeAllocated(entropy);
            FreeLocal(output);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The internal raw trace key requires Windows DPAPI.");
        }
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return default;
        }

        IntPtr pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { ByteCount = bytes.Length, Data = pointer };
    }

    private static byte[] Copy(DataBlob blob)
    {
        if (blob.ByteCount == 0 || blob.Data == IntPtr.Zero)
        {
            return [];
        }

        var bytes = new byte[blob.ByteCount];
        Marshal.Copy(blob.Data, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void FreeAllocated(DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.Data);
        }
    }

    private static void FreeLocal(DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
        {
            _ = LocalFree(blob.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        internal int ByteCount;
        internal IntPtr Data;
    }

    [DllImport("Crypt32.dll", EntryPoint = "CryptProtectData", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("Crypt32.dll", EntryPoint = "CryptUnprotectData", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
