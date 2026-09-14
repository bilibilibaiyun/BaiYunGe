using System.Windows;

namespace BaiYunGe.Core.Output;

public enum OutputResult
{
    SentToTarget,
    CopiedToClipboard,
    Failed
}

/// <summary>
/// 识别文本输出：焦点仍在目标窗口时，无条件优先 SendInput Unicode 全局注入
/// （不依赖「目标是否可编辑」的判断，也不依赖剪贴板）；失败才复制剪贴板并自动粘贴
/// （WM_PASTE → Ctrl+V）。焦点丢失则仅复制剪贴板。全程不激活本程序窗口。
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

        // SendInput 是全局键盘注入：只要焦点仍在原窗口，就直接把 Unicode 文本敲进去，
        // 无需预先判断目标控件是否「可编辑」（对浏览器/聊天框/编辑器/终端均有效），
        // 也不依赖剪贴板（规避剪贴板被占用的问题）。
        if (targetStillFocused && User32.SendUnicodeText(text))
        {
            _logger.Info("Sent text via SendInput (Unicode).");
            return OutputResult.SentToTarget;
        }

        if (!targetStillFocused)
        {
            _logger.Warn(
                $"Focus lost (expected=0x{targetWindow.ToInt64():X}, actual=0x{foreground.ToInt64():X}); clipboard only.");
        }

        // 兜底：复制剪贴板 + 自动粘贴。
        var copied = await CopyToClipboardAsync(text, cancellationToken);
        if (copied != OutputResult.CopiedToClipboard)
        {
            return OutputResult.Failed;
        }

        if (targetStillFocused)
        {
            var focus = User32.GetFocusedWindow(foreground);
            if (User32.SendWmPaste(focus))
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
        for (var attempt = 0; attempt < 15; attempt++)
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
