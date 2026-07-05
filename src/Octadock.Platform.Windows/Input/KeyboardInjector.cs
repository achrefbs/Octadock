using System.Runtime.Versioning;

namespace Octadock.Platform.Windows.Input;

/// <summary>
/// Synthesizes keystrokes into the foreground window via <c>SendInput</c>. Used by
/// dictation to paste the transcript at the cursor (Ctrl+V). Returns false when the
/// OS swallows the input — for example an elevated foreground window rejecting a
/// non-elevated sender (UIPI) — so callers can fall back to clipboard-only.
/// </summary>
[SupportedOSPlatform("windows")]
public static class KeyboardInjector
{
    private const int InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private static readonly int InputSize = global::System.Runtime.InteropServices.Marshal.SizeOf<INPUT>();

    /// <summary>
    /// Waits (bounded) until every physical modifier key is released. Called
    /// before pasting: with hold-to-talk the user is often still holding
    /// Ctrl+Shift at release, which would turn the injected Ctrl+V into
    /// Ctrl+Shift+V in the target app. Returns true when all modifiers are up.
    /// </summary>
    public static bool WaitForModifierRelease(TimeSpan timeout)
    {
        // VK_SHIFT, VK_CONTROL, VK_MENU (Alt), VK_LWIN, VK_RWIN.
        static bool AnyModifierDown()
            => ((GetAsyncKeyState(0x10) | GetAsyncKeyState(0x11) | GetAsyncKeyState(0x12)
                 | GetAsyncKeyState(0x5B) | GetAsyncKeyState(0x5C)) & 0x8000) != 0;

        long deadline = global::System.Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (AnyModifierDown())
        {
            if (global::System.Environment.TickCount64 >= deadline)
            {
                return false;
            }

            global::System.Threading.Thread.Sleep(15);
        }

        return true;
    }

    /// <summary>
    /// Sends Ctrl+V (key down Ctrl, down V, up V, up Ctrl) as one atomic
    /// <c>SendInput</c> batch. Returns true only when all four events were injected.
    /// </summary>
    public static bool SendPaste()
    {
        var inputs = new INPUT[4];
        inputs[0] = KeyDown(VkControl);
        inputs[1] = KeyDown(VkV);
        inputs[2] = KeyUp(VkV);
        inputs[3] = KeyUp(VkControl);

        uint sent = SendInput((uint)inputs.Length, inputs, InputSize);
        return sent == inputs.Length;
    }

    private static INPUT KeyDown(ushort vk) => MakeKey(vk, 0);

    private static INPUT KeyUp(ushort vk) => MakeKey(vk, KeyEventFKeyUp);

    private static INPUT MakeKey(ushort vk, uint flags) => new()
    {
        Type = InputKeyboard,
        U = new INPUTUNION
        {
            Keyboard = new KEYBDINPUT
            {
                Vk = vk,
                Scan = 0,
                Flags = flags,
                Time = 0,
                ExtraInfo = global::System.IntPtr.Zero,
            },
        },
    };

    [global::System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [global::System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [global::System.Runtime.InteropServices.StructLayout(
        global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct INPUT
    {
        public int Type;
        public INPUTUNION U;
    }

    [global::System.Runtime.InteropServices.StructLayout(
        global::System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [global::System.Runtime.InteropServices.FieldOffset(0)]
        public KEYBDINPUT Keyboard;

        [global::System.Runtime.InteropServices.FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [global::System.Runtime.InteropServices.FieldOffset(0)]
        public HARDWAREINPUT Hardware;
    }

    [global::System.Runtime.InteropServices.StructLayout(
        global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public global::System.IntPtr ExtraInfo;
    }

    [global::System.Runtime.InteropServices.StructLayout(
        global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public global::System.IntPtr ExtraInfo;
    }

    [global::System.Runtime.InteropServices.StructLayout(
        global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Msg;
        public ushort ParamL;
        public ushort ParamH;
    }
}
