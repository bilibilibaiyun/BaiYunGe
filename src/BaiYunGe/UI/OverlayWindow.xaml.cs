using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BaiYunGe.UI;

/// <summary>
/// 不抢焦点的状态浮窗：显示聆听/识别中/已输入/错误。不进入任务栏、鼠标穿透、
/// 尺寸随文本自适应（用 FormattedText 精确测量换行高度，避免丢失最后一行）。
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;

    private const double MaxTextWidth = 420;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyExtendedStyles();
    }

    public void ShowStatus(string status, double level, string timeText)
    {
        StatusText.Text = status;
        LevelBar.Value = Math.Clamp(level, 0, 1);
        TimeText.Text = timeText;
        FixTextBlockHeight(status);
    }

    public void ShowListening(double level, string timeText)
    {
        ShowStatus(string.Empty, level, timeText);
    }

    public void ShowMessage(string message)
    {
        ShowStatus(message, 0, string.Empty);
    }

    private void ApplyExtendedStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLong(handle, GwlExStyle);
        style |= WsExNoActivate | WsExToolWindow | WsExTransparent;
        SetWindowLong(handle, GwlExStyle, style);
    }

    /// <summary>
    /// WPF 文本测量与渲染存在舍入差异，仅靠布局自适应会丢最后一行；
    /// 这里用 FormattedText 精确计算换行高度并固定文本块高度。
    /// </summary>
    private void FixTextBlockHeight(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            StatusText.Height = double.NaN;
            return;
        }

        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(StatusText.FontFamily, StatusText.FontStyle, StatusText.FontWeight, StatusText.FontStretch),
            StatusText.FontSize,
            System.Windows.Media.Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        formatted.MaxTextWidth = MaxTextWidth;
        StatusText.Width = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
        StatusText.Height = Math.Ceiling(formatted.Height);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        Hide();
        e.Cancel = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
