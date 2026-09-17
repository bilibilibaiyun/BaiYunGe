using System.Threading;
using System.Windows;

namespace BaiYunGe.Core.Output;

public enum OutputResult
{
    SentToTarget,
    CopiedToClipboard,
    Failed
}

/// <summary>
/// 识别文本输出：焦点仍在目标窗口且目标控件可编辑时，优先 SendInput Unicode 全局注入
/// （不依赖剪贴板）；失败才复制剪贴板并自动粘贴（WM_PASTE → Ctrl+V）。
/// 焦点丢失或目标不可编辑则仅复制剪贴板。全程不激活本程序窗口。
/// </summary>
public sealed class TextOutputService
{
    private readonly AppLogger _logger;

    public TextOutputService(AppLogger logger)
    {
        _logger = logger;
    }

    public async Task<OutputResult> OutputAsync(
        string text,
        IntPtr targetWindow,
        string outputMethod,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return OutputResult.Failed;
        }

        var foreground = User32.GetForegroundWindow();
        var targetStillFocused = targetWindow != IntPtr.Zero && foreground == targetWindow;
        var focusWindow = User32.GetFocusedWindow(foreground);

        // 黑名单策略：只排除明确不可编辑的桌面/资源管理器外壳，其余一律默认键入。
        // 不用 UIA 精确区分「输入框 vs 空白处」——那会误判正常输入目标（浏览器页面、
        // 自定义控件等）导致本该键入的走了剪贴板；浮窗提示已统一为「已输入 / 已复制」，
        // 无需精确区分。
        var editable = !InputFieldDetector.IsKnownNonEditable(focusWindow) &&
                       !InputFieldDetector.IsKnownNonEditable(foreground);

        // SendInput 是全局键盘注入：只要焦点仍在原窗口且焦点控件可编辑，
        // 就直接把 Unicode 文本敲进去（对浏览器/聊天框/编辑器/终端均有效），
        // 也不依赖剪贴板（规避剪贴板被占用的问题）。
        if (targetStillFocused && editable && User32.SendUnicodeText(text))
        {
            _logger.Info("Sent text via SendInput (Unicode).");
            return OutputResult.SentToTarget;
        }

        if (!targetStillFocused)
        {
            _logger.Warn(
                $"Focus lost (expected=0x{targetWindow.ToInt64():X}, actual=0x{foreground.ToInt64():X}); clipboard only.");
        }
        else if (!editable)
        {
            _logger.Info("Target is not editable; skipping direct input and auto-paste, clipboard only.");
        }

        // 兜底：复制剪贴板 + 自动粘贴。
        var copied = await CopyToClipboardAsync(text, cancellationToken);
        if (copied != OutputResult.CopiedToClipboard)
        {
            return OutputResult.Failed;
        }

        if (targetStillFocused && editable)
        {
            if (User32.SendWmPaste(focusWindow))
            {
                _logger.Info("Pasted text via WM_PASTE.");
                return OutputResult.SentToTarget;
            }

            if (User32.SendCtrlV())
            {
                _logger.Info("Pasted text via Ctrl+V.");
                return OutputResult.SentToTarget;
            }
        }

        _logger.Warn("Auto-paste failed; text remains on clipboard.");
        return OutputResult.CopiedToClipboard;
    }

    private async Task<OutputResult> CopyToClipboardAsync(string text, CancellationToken cancellationToken)
    {
        // 剪贴板被外部进程（远程桌面/剪贴板工具）占用时 OpenClipboard 可能阻塞很久。
        // 在独立 STA 线程限时执行（每次最多 1 秒），避免阻塞 UI 线程导致浮窗卡在「识别中」；
        // 重试 2 次给剪贴板释放的机会，失败快速返回并明确提示。
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (TrySetClipboardText(text, 1000))
            {
                _logger.Info("Text copied to clipboard.");
                return OutputResult.CopiedToClipboard;
            }

            _logger.Warn($"Clipboard busy (attempt {attempt + 1}).");
            await Task.Delay(200, cancellationToken);
        }

        _logger.Error("Failed to copy text to clipboard after retries.");
        return OutputResult.Failed;
    }

    /// <summary>
    /// 在独立 STA 线程执行剪贴板写入并限时。超时（剪贴板仍被占用）返回 false，
    /// 后台线程继续阻塞但作为 background 线程不影响 UI。
    /// </summary>
    private static bool TrySetClipboardText(string text, int timeoutMs)
    {
        var result = false;
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetText(text);
                result = true;
            }
            catch
            {
                result = false;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(timeoutMs))
        {
            return false;
        }

        return result;
    }
}
