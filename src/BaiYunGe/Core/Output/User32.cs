using System.Runtime.InteropServices;

namespace BaiYunGe.Core.Output;

/// <summary>
/// Win32 互操作：SendInput（Unicode 键入 / Ctrl+V 粘贴）、跨进程焦点获取、
/// WM_PASTE 消息粘贴。结构体布局与 Windows x64 完全对齐（sizeof(INPUT)=40）。
/// </summary>
internal static class User32
{
    internal const uint InputKeyboard = 1;
    internal const uint KeyEventfKeyUp = 0x0002;
    internal const uint KeyEventfUnicode = 0x0004;
    internal const uint WmPaste = 0x0302;
    internal const ushort VkControl = 0x11;
    internal const ushort VkV = 0x56;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo pgui);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint cInputs, [In] Input[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>获取指定窗口（通常为前台窗口）所在线程真正的键盘焦点窗口，跨进程有效。</summary>
    internal static IntPtr GetFocusedWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
        {
            return IntPtr.Zero;
        }

        var threadId = GetWindowThreadProcessId(windowHandle, out _);
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(threadId, ref info) ? info.hwndFocus : IntPtr.Zero;
    }

    /// <summary>通过 SendInput 以 Unicode 方式键入文本，返回是否全部事件被系统接受。</summary>
    internal static bool SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var inputs = new Input[text.Length * 2];
        var index = 0;
        foreach (var character in text)
        {
            if (character is '\r' or '\n')
            {
                inputs[index++] = VirtualKeyInput(0x0D, false);
                inputs[index++] = VirtualKeyInput(0x0D, true);
                continue;
            }

            if (character == '\t')
            {
                inputs[index++] = VirtualKeyInput(0x09, false);
                inputs[index++] = VirtualKeyInput(0x09, true);
                continue;
            }

            inputs[index++] = UnicodeInput(character, false);
            inputs[index++] = UnicodeInput(character, true);
        }

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        return sent == inputs.Length;
    }

    /// <summary>通过 SendInput 模拟 Ctrl+V 粘贴，返回是否全部事件被系统接受。</summary>
    internal static bool SendCtrlV()
    {
        var inputs = new[]
        {
            VirtualKeyInput(VkControl, false),
            VirtualKeyInput(VkV, false),
            VirtualKeyInput(VkV, true),
            VirtualKeyInput(VkControl, true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        return sent == inputs.Length;
    }

    /// <summary>向目标窗口发送 WM_PASTE 消息（对标准 Edit/RichEdit 控件有效）。</summary>
    internal static bool SendWmPaste(IntPtr focusWindow)
    {
        if (focusWindow == IntPtr.Zero || !IsWindow(focusWindow))
        {
            return false;
        }

        SendMessageW(focusWindow, WmPaste, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    private static Input UnicodeInput(char character, bool keyUp)
    {
        return new Input
        {
            type = InputKeyboard,
            union = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wScan = character,
                    dwFlags = KeyEventfUnicode | (keyUp ? KeyEventfKeyUp : 0)
                }
            }
        };
    }

    private static Input VirtualKeyInput(ushort virtualKey, bool keyUp)
    {
        return new Input
        {
            type = InputKeyboard,
            union = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wVk = virtualKey,
                    dwFlags = keyUp ? KeyEventfKeyUp : 0
                }
            }
        };
    }

    // ---- 结构体：布局与 Windows x64 对齐（sizeof(INPUT)=40，union 按最大成员 MOUSEINPUT=32） ----

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        internal int cbSize;
        internal uint flags;
        internal IntPtr hwndActive;
        internal IntPtr hwndFocus;
        internal IntPtr hwndCapture;
        internal IntPtr hwndMenuOwner;
        internal IntPtr hwndMoveSize;
        internal IntPtr hwndCaret;
        internal Rect rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct Input
    {
        [FieldOffset(0)]
        internal uint type;

        [FieldOffset(8)]
        internal InputUnion union;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        internal MouseInput mi;

        [FieldOffset(0)]
        internal KeyboardInput ki;

        [FieldOffset(0)]
        internal HardwareInput hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        internal int dx;
        internal int dy;
        internal uint mouseData;
        internal uint dwFlags;
        internal uint time;
        internal IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInput
    {
        internal uint uMsg;
        internal ushort wParamL;
        internal ushort wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        internal ushort wVk;
        internal ushort wScan;
        internal uint dwFlags;
        internal uint time;
        internal IntPtr dwExtraInfo;
    }
}
