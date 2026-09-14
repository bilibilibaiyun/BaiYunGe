namespace BaiYunGe.UI;

public enum AppLanguage
{
    ZhCn,
    EnUs
}

/// <summary>
/// 集中式本地化字符串表，支持中英运行时切换。
/// 占位符一律用 {0}/{1}（string.Format 风格），避免命名占位符导致的 FormatException。
/// </summary>
public sealed class LocalizedText
{
    private static readonly Dictionary<string, (string Zh, string En)> Table = new()
    {
        ["Tray.OpenSettings"] = ("打开设置", "Open Settings"),
        ["Tray.PauseInput"] = ("暂停输入", "Pause Input"),
        ["Tray.ResumeInput"] = ("恢复输入", "Resume Input"),
        ["Tray.Exit"] = ("退出", "Exit"),

        ["Settings.Title"] = ("白云歌 设置", "BaiYunGe Settings"),
        ["Page.General"] = ("通用", "General"),
        ["Page.Shortcut"] = ("快捷键", "Shortcut"),
        ["Page.Dictionary"] = ("词典", "Dictionary"),
        ["Page.Model"] = ("本地模型", "Local Model"),

        ["Shortcut.Current"] = ("当前快捷键", "Current Shortcut"),
        ["Shortcut.Record"] = ("录制快捷键", "Record Shortcut"),
        ["Shortcut.Recording"] = ("请按下新的快捷键组合（Esc 取消）", "Press a new shortcut (Esc to cancel)"),
        ["Shortcut.Mode"] = ("触发方式", "Trigger Mode"),
        ["Shortcut.ModeHold"] = ("按住说话，松开识别", "Hold to talk, release to transcribe"),
        ["Shortcut.ModeToggle"] = ("按一下开始，再按/静音结束", "Press to start, press again or silence to stop"),

        ["General.Language"] = ("界面语言", "Language"),
        ["General.Theme"] = ("外观", "Theme"),
        ["General.ThemeDark"] = ("夜间", "Dark"),
        ["General.ThemeLight"] = ("日间", "Light"),
        ["General.AutoStart"] = ("开机自启", "Start with Windows"),
        ["General.Mic"] = ("麦克风", "Microphone"),
        ["General.TestMic"] = ("测试麦克风", "Test Microphone"),
        ["General.Device"] = ("推理设备", "Inference Device"),
        ["General.DeviceGpu"] = ("GPU 优先（Vulkan）", "GPU first (Vulkan)"),
        ["General.DeviceCpu"] = ("仅 CPU", "CPU only"),

        ["Model.Directory"] = ("模型目录", "Model Directory"),
        ["Model.Browse"] = ("浏览", "Browse"),
        ["Model.Install"] = ("安装模型", "Install Model"),
        ["Model.Installed"] = ("模型已就绪", "Model is ready"),
        ["Model.NotInstalled"] = ("模型未安装", "Model not installed"),
        ["Model.Progress"] = ("下载 {0}：{1} / {2}", "Downloading {0}: {1} / {2}"),
        ["Model.Downloading"] = ("下载中…", "Downloading…"),

        ["Dict.Enabled"] = ("启用词典", "Enable dictionary"),
        ["Dict.Alias"] = ("别名", "Alias"),
        ["Dict.Target"] = ("目标词", "Target"),
        ["Dict.Add"] = ("添加", "Add"),
        ["Dict.Remove"] = ("删除", "Remove"),

        ["Overlay.Listening"] = ("聆听中…", "Listening…"),
        ["Overlay.Transcribing"] = ("识别中…", "Transcribing…"),
        ["Overlay.Done"] = ("已输入", "Done"),
        ["Overlay.Copied"] = ("已复制到剪贴板", "Copied to clipboard"),
        ["Overlay.NoSpeech"] = ("未检测到语音", "No speech detected"),
        ["Overlay.Cancelled"] = ("已取消", "Cancelled"),
        ["Overlay.Error"] = ("错误", "Error"),

        ["FirstRun.Title"] = ("首次运行", "First Run"),
        ["FirstRun.Message"] = ("请选择模型保存目录（建议放在 D 盘）", "Please choose a model directory (D: drive recommended)"),

        ["Common.OK"] = ("确定", "OK"),
        ["Common.Cancel"] = ("取消", "Cancel"),
        ["Common.Apply"] = ("应用", "Apply"),
        ["Common.Close"] = ("关闭", "Close")
    };

    private AppLanguage _language = AppLanguage.ZhCn;

    public AppLanguage Language => _language;

    public event EventHandler? LanguageChanged;

    public void SetLanguage(AppLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        if (Table.TryGetValue(key, out var pair))
        {
            return _language == AppLanguage.ZhCn ? pair.Zh : pair.En;
        }

        return key;
    }

    public string Format(string key, params object?[] args)
    {
        return string.Format(Get(key), args);
    }
}
