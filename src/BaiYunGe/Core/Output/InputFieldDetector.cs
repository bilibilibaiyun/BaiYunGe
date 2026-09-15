using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace BaiYunGe.Core.Output;

/// <summary>判断窗口/控件是否可接收文本输入（UIA 控件类型 + ValuePattern/TextPattern + 窗口类联合策略）。</summary>
public static class InputFieldDetector
{
    private static readonly HashSet<string> EditableWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit",
        "RichEdit",
        "RichEdit20A",
        "RichEdit20W",
        "RichEdit50W",
        "RICHEDIT50W",
        "Scintilla",
        "TEdit",
        "TMemo",
        "ATL:Edit",
        "WindowsForms10.EDIT.app"
    };

    private static readonly HashSet<string> NonEditableWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SysListView32",
        "SHELLDLL_DefView",
        "Progman",
        "WorkerW",
        "DirectUIHWND",
        "SysTreeView32"
    };

    /// <summary>
    /// 黑名单判断：明确不可编辑的目标（桌面图标列表/桌面/资源管理器外壳）返回 true；
    /// 其余（含 UIA 失败、Edit/Document、终端等）一律返回 false（视为可键入）。
    /// </summary>
    public static bool IsKnownNonEditable(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var className = GetClassName(hwnd);
        return !string.IsNullOrEmpty(className) && NonEditableWindowClasses.Contains(className);
    }

    public static bool IsEditableTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            if (element is not null && IsEditableElement(element))
            {
                return true;
            }
        }
        catch
        {
            // UIA 不可用时回退到窗口类判断。
        }

        var className = GetClassName(hwnd);
        return !string.IsNullOrEmpty(className) && EditableWindowClasses.Contains(className);
    }

    private static bool IsEditableElement(AutomationElement element)
    {
        var controlType = element.Current.ControlType;

        if (controlType == ControlType.Edit ||
            controlType == ControlType.Document)
        {
            return true;
        }

        // 组合框本身不可直接写，需看其内部 Edit；这里保守地视为不可编辑，
        // 由窗口类回退策略兜底。
        if (controlType == ControlType.ComboBox)
        {
            return false;
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern))
        {
            return !((ValuePattern)valuePattern).Current.IsReadOnly;
        }

        // 不再用 TextPattern 判断可编辑：只读的列表/文本控件（如桌面图标列表、
        // 资源管理器文件列表、Static 文本、Tree）同样支持 TextPattern，会误判为可编辑。
        return false;
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        var length = GetClassName(hwnd, builder, builder.Capacity);
        return length > 0 ? builder.ToString() : string.Empty;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
