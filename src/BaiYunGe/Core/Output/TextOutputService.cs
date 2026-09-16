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
        // 黑名单策略：焦点控件或顶层窗口命中黑名单（桌面/资源管理器外壳）→ 不可编辑复制；
        // 其余（含 focusWindow 获取失败但 foreground 是正常应用）→ 默认键入。
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
        // 只重试 3 次：剪贴板被外部进程（远程桌面/剪贴板工具）持续占用时，每次 OpenClipboard
        // 会阻塞约 1 秒，重试 15 次会让浮窗卡在「识别中」十几秒。快速失败并明确提示更友好。
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                _logger.Info("Text copied to clipboard.");
                return OutputResult.CopiedToClipboard;
            }
            catch (Exception exception)
            {
                _logger.Warn($"Clipboard busy (attempt {attempt + 1}): {exception.Message}");
            }

            // 保持当前同步上下文（UI/STA 线程），否则重试会在线程池 MTA 线程执行导致 OLE/剪贴板失败。
            await Task.Delay(150, cancellationToken);
        }

        _logger.Error("Failed to copy text to clipboard after retries.");
        return OutputResult.Failed;
    }
}
