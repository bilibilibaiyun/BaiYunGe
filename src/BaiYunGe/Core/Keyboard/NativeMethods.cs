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

    public static bool IsModifier(int vkCode)
    {
        return vkCode is VK_LCONTROL or VK_RCONTROL or VK_LSHIFT or VK_RSHIFT or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN;
    }

    public static (bool Ctrl, bool Alt, bool Shift, bool Win) ModifierState()
    {
        static bool Down(int vk) => (GetKeyState(vk) & 0x8000) != 0;

        return (
            Down(VK_LCONTROL) || Down(VK_RCONTROL),
            Down(VK_LMENU) || Down(VK_RMENU),
            Down(VK_LSHIFT) || Down(VK_RSHIFT),
            Down(VK_LWIN) || Down(VK_RWIN));
    }
}
