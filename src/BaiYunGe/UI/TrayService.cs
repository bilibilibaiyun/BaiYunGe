using System.Drawing;
using System.Windows.Forms;
using BaiYunGe.Core;

namespace BaiYunGe.UI;

/// <summary>系统托盘：静默常驻，右键菜单打开设置/暂停/退出。</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly LocalizedText _text;
    private ToolStripMenuItem? _pauseItem;

    private bool _paused;

    public TrayService(LocalizedText text)
    {
        _text = text;
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "白云歌 BaiYunGe"
        };

        _notifyIcon.ContextMenuStrip = BuildMenu();
        _notifyIcon.DoubleClick += (_, _) => OpenSettings?.Invoke(this, EventArgs.Empty);

        text.LanguageChanged += (_, _) => _notifyIcon.ContextMenuStrip = BuildMenu();
    }

    public event EventHandler? OpenSettings;

    public event EventHandler? ExitRequested;

    public event EventHandler<bool>? PauseChanged;

    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (_pauseItem is not null)
        {
            _pauseItem.Text = _text.Get(paused ? "Tray.ResumeInput" : "Tray.PauseInput");
        }
    }

    /// <summary>启动后弹出托盘气泡，提示应用已最小化运行。</summary>
    public void ShowStartupBalloon()
    {
        try
        {
            _notifyIcon.ShowBalloonTip(3000, _text.Get("Tray.StartupTipTitle"), _text.Get("Tray.StartupTipText"), ToolTipIcon.Info);
        }
        catch
        {
            // 气泡失败不影响运行。
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var open = new ToolStripMenuItem(_text.Get("Tray.OpenSettings"));
        open.Click += (_, _) => OpenSettings?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(open);

        _pauseItem = new ToolStripMenuItem(_text.Get(_paused ? "Tray.ResumeInput" : "Tray.PauseInput"));
        _pauseItem.Click += (_, _) =>
        {
            _paused = !_paused;
            _pauseItem.Text = _text.Get(_paused ? "Tray.ResumeInput" : "Tray.PauseInput");
            PauseChanged?.Invoke(this, _paused);
        };
        menu.Items.Add(_pauseItem);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem(_text.Get("Tray.Exit"));
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exit);

        return menu;
    }

    private static Icon LoadIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppPaths.AppDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                // 显式取 32x32 帧，托盘在高 DPI 下显示更清晰（默认取 16x16 会偏小模糊）。
                return new Icon(iconPath, 32, 32);
            }
        }
        catch
        {
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
