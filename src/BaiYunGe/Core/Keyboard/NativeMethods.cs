using System.Runtime.InteropServices;

namespace BaiYunGe.Core.Keyboard;

internal static class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LMENU = 0xA4;
    public const int VK_RMENU = 0xA5;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_ESCAPE = 0x1B;

    public const int LLKHF_EXTENDED = 0x01;
    public const int LLKHF_INJECTED = 0x10;
    public const int LLKHF_ALTDOWN = 0x20;
    public const int LLKHF_UP = 0x80;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLowLevelHookStruct
    {
        public int VkCode;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    /// <summary>查询键的实时物理按下状态（GetAsyncKeyState 高位，与线程消息队列无关）。</summary>
    public static bool IsKeyDown(int vkCode)
    {
        return (GetAsyncKeyState(vkCode) & 0x8000) != 0;
    }

    public static bool IsModifier(int vkCode)
    {
        return vkCode is VK_LCONTROL or VK_RCONTROL or VK_LSHIFT or VK_RSHIFT or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN;
    }

    /// <summary>纯修饰键（Ctrl/Alt/Shift），不可作为主键。</summary>
    public static bool IsCtrlAltShift(int vkCode)
    {
        return vkCode is VK_LCONTROL or VK_RCONTROL or VK_LSHIFT or VK_RSHIFT or VK_LMENU or VK_RMENU;
    }

    /// <summary>Win 键（可作为修饰键，也可作为主键，如 Ctrl+Win）。</summary>
    public static bool IsWinKey(int vkCode)
    {
        return vkCode is VK_LWIN or VK_RWIN;
    }

    public static (bool Ctrl, bool Alt, bool Shift, bool Win) ModifierState()
    {
        // 用 GetAsyncKeyState（硬件实时状态），而非 GetKeyState（线程消息队列状态）。
        // 低级键盘钩子回调里 GetKeyState 可能返回陈旧状态，导致「同时按下修饰键+主键」
        // 时主键 keydown 先到、查询修饰键却显示未按下，组合唤醒失败。
        return (
            IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL),
            IsKeyDown(VK_LMENU) || IsKeyDown(VK_RMENU),
            IsKeyDown(VK_LSHIFT) || IsKeyDown(VK_RSHIFT),
            IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN));
    }
}
