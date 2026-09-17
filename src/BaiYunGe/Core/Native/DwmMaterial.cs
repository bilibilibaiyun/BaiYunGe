using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BaiYunGe.Core.Native;

/// <summary>
/// DWM 窗口材质（亚克力 Acrylic / 云母 Mica）：为窗口应用半透明模糊背景材质，
/// 实现类似 iOS 液态玻璃的「背后内容模糊」效果。
/// Windows 11 优先用云母（Mica），Windows 10 回退到亚克力（Acrylic）。
/// </summary>
public static class DwmMaterial
{
    // Windows 11 Mica（云母）：DWMWA_SYSTEMBACKDROP_TYPE = 38。
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtMainWindow = 2;       // Mica
    private const int DwmsbtTransientWindow = 3;  // Mica Alt（弹窗更合适）

    // Windows 10 Acrylic（亚克力）：SetWindowCompositionAttribute。
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int WcaAccentPolicy = 19;

    /// <summary>为窗口应用材质。窗口可能尚未创建句柄，此时挂到 SourceInitialized。</summary>
    public static void ApplyToWindow(Window window, bool dark, bool transient = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            window.SourceInitialized += (_, _) => ApplyToHwnd(window, dark, transient);
            return;
        }

        ApplyToHwnd(window, dark, transient);
    }

    private static void ApplyToHwnd(Window window, bool dark, bool transient)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!TryApplyMica(hwnd, transient))
        {
            TryApplyAcrylic(hwnd, dark);
        }
    }

    private static bool TryApplyMica(IntPtr hwnd, bool transient)
    {
        // Mica 仅 Windows 11（Build 22000+）。
        if (Environment.OSVersion.Version.Build < 22000)
        {
            return false;
        }

        var value = transient ? DwmsbtTransientWindow : DwmsbtMainWindow;
        return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref value, sizeof(int)) == 0;
    }

    private static bool TryApplyAcrylic(IntPtr hwnd, bool dark)
    {
        var accent = new AccentPolicy
        {
            AccentState = AccentEnableAcrylicBlurBehind,
            AccentFlags = 2, // 绘制所有边框。
            // AABBGGRR：日间半透明白、夜间半透明深蓝灰。
            GradientColor = dark ? 0xCC241C1Cu : 0xCCFFFFFFu,
            AnimationId = 0
        };

        var accentSize = Marshal.SizeOf<AccentPolicy>();
        var accentPtr = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = accentPtr,
                SizeOfData = accentSize
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
