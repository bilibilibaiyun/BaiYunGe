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
        ["Tray.StartupTipTitle"] = ("白云歌已启动", "BaiYunGe started"),
        ["Tray.StartupTipText"] = ("白云歌已最小化启动", "BaiYunGe is minimized"),

        ["Settings.Title"] = ("白云歌 设置", "BaiYunGe Settings"),
        ["Page.General"] = ("通用", "General"),
        ["Page.Shortcut"] = ("快捷键", "Shortcut"),
        ["Page.Dictionary"] = ("词典", "Dictionary"),
        ["Page.Model"] = ("本地模型", "Local Model"),
        ["Page.Calibration"] = ("环境采样", "Calibration"),
        ["Calibration.Hint"] = ("适用于固定环境：在安静或嘈杂的固定环境中，录制一段你平时放松说话的音频，软件会自动分析并设定最适合的检测阈值。", "For a fixed environment: record a short clip of yourself speaking naturally; the app analyzes it and sets the most suitable detection threshold."),
        ["Calibration.Start"] = ("开始采样", "Start Calibration"),
        ["Calibration.Prompt"] = ("点击确定后，请用平时说话的音量，连续说五遍「你好、你好、你好、你好、你好」。", "After clicking OK, please say \"hello\" five times in a row at your normal speaking volume."),
        ["Calibration.Recording"] = ("正在录音采样…\n请连续说五遍「你好」", "Recording…\nPlease say \"hello\" five times"),
        ["Calibration.Current"] = ("当前采样阈值：{0:F1} dB", "Current calibrated threshold: {0:F1} dB"),
        ["Calibration.None"] = ("尚未采样（使用环境模式阈值）", "Not calibrated yet (using environment-mode threshold)"),
        ["Calibration.Done"] = ("采样完成，检测阈值已设为 {0:F1} dB。", "Calibration done. Detection threshold set to {0:F1} dB."),
        ["Calibration.Failed"] = ("采样分析失败：请确保说话时长足够，且环境相对稳定后重试。", "Calibration failed: please ensure you spoke long enough and the environment is stable, then retry."),

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
        ["General.NoiseEnv"] = ("环境模式", "Noise environment"),
        ["General.NoiseAuto"] = ("自动（动态自适应）", "Auto (adaptive)"),
        ["General.NoiseQuiet"] = ("安静（低阈值，轻声易触发）", "Quiet (low threshold)"),
        ["General.NoiseNoisy"] = ("嘈杂（高阈值，过滤噪音）", "Noisy (high threshold)"),
        ["General.InputGain"] = ("输入增益（补偿降噪麦克风）", "Input gain (compensate noise-cancelling mic)"),
        ["General.GainOff"] = ("关闭（不增益）", "Off"),
        ["General.Device"] = ("推理设备", "Inference Device"),
        ["General.DeviceGpu"] = ("GPU 优先（Vulkan）", "GPU first (Vulkan)"),
        ["General.DeviceCpu"] = ("仅 CPU", "CPU only"),
        ["General.RecognitionLanguage"] = ("识别语言", "Recognition Language"),
        ["General.LangAuto"] = ("自动检测", "Auto detect"),
        ["General.LangZh"] = ("中文", "Chinese"),
        ["General.LangEn"] = ("英文", "English"),

        ["Model.Directory"] = ("模型目录", "Model Directory"),
        ["Model.Browse"] = ("浏览", "Browse"),
        ["Model.Install"] = ("安装模型", "Install Model"),
        ["Model.Installed"] = ("模型已就绪", "Model is ready"),
        ["Model.NotInstalled"] = ("模型未安装", "Model not installed"),
        ["Model.Progress"] = ("下载 {0}：{1} / {2}", "Downloading {0}: {1} / {2}"),
        ["Model.Verifying"] = ("校验 {0}：{1} / {2}", "Verifying {0}: {1} / {2}"),
        ["Model.Downloading"] = ("下载中…", "Downloading…"),

        ["Dict.Enabled"] = ("启用词典", "Enable dictionary"),
        ["Dict.Alias"] = ("别名", "Alias"),
        ["Dict.Target"] = ("目标词", "Target"),
        ["Dict.Add"] = ("添加", "Add"),
        ["Dict.Remove"] = ("删除", "Remove"),

        ["Overlay.Listening"] = ("倾听中…", "Listening…"),
        ["Overlay.Transcribing"] = ("转化中…", "Transcribing…"),
        ["Overlay.WarmingUp"] = ("模型预热中…", "Warming up model…"),
        ["Overlay.Done"] = ("已输入 / 已复制", "Input / Copied"),
        ["Overlay.Copied"] = ("已复制到剪贴板", "Copied to clipboard"),
        ["Overlay.CopyFailed"] = ("复制失败（剪贴板被占用）", "Copy failed (clipboard busy)"),
        ["Overlay.NoSpeech"] = ("未检测到语音", "No speech detected"),
        ["Overlay.Cancelled"] = ("已取消", "Cancelled"),
        ["Overlay.Error"] = ("错误", "Error"),

        ["FirstRun.Title"] = ("首次运行", "First Run"),
        ["FirstRun.Message"] = ("请选择模型保存目录（建议放在 D 盘）", "Please choose a model directory (D: drive recommended)"),

        ["Update.Title"] = ("检查更新", "Check for Updates"),
        ["Update.Check"] = ("检查更新", "Check for Updates"),
        ["Update.Checking"] = ("正在检查更新…", "Checking for updates…"),
        ["Update.UpToDate"] = ("已是最新版本", "You are up to date"),
        ["Update.Failed"] = ("检查更新失败（网络不可用）", "Update check failed (network unavailable)"),
        ["Update.NewVersion"] = ("发现新版本", "New version available"),
        ["Update.UpdateNow"] = ("立即更新", "Update Now"),
        ["Update.AutoCheck"] = ("启动时自动检查更新", "Check for updates at startup"),
        ["Update.CurrentVersion"] = ("当前版本", "Current version"),
        ["Update.LatestVersion"] = ("最新版本", "Latest version"),
        ["Update.Downloading"] = ("正在下载更新 {0} / {1}", "Downloading update {0} / {1}"),
        ["Update.Confirm"] = ("是否立即更新？更新会保留你的词典、主题等配置。", "Update now? Your dictionary, theme and other settings will be kept."),
        ["Update.ReadyToInstall"] = ("更新已下载完成。\n\n软件即将退出，随后可能会弹出 Windows 用户账户控制（UAC）窗口，请点击「是」以完成安装。", "Update downloaded.\n\nThe app will now exit. If a Windows User Account Control (UAC) prompt appears, click \"Yes\" to complete the installation."),
        ["Update.Rollback"] = ("版本回退…", "Rollback version…"),
        ["Update.RollbackHint"] = ("选择一个要回退到的历史版本：", "Select a previous version to roll back to:"),
        ["Update.RollbackConfirm"] = ("确定回退到 v{0} 吗？\n\n回退会下载该版本的安装包并覆盖安装，你的词典、主题等配置会保留。", "Roll back to v{0}?\n\nThe installer for this version will be downloaded and installed, keeping your dictionary, theme and other settings."),
        ["Update.RollbackAction"] = ("回退到此版本", "Roll back to this version"),
        ["Update.Cancel"] = ("取消", "Cancel"),
        ["Update.Current"] = ("当前", "current"),
        ["Update.Stable"] = ("稳定版", "Stable"),
        ["Update.Test"] = ("测试版", "Testing"),
        ["Update.TestWarning"] = ("建议选择稳定版本，测试版本可能有缺陷", "Stable versions are recommended; testing versions may have defects."),

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
