using System.Text.Json.Serialization;

namespace BaiYunGe.Core;

public sealed record DictionaryEntry
{
    public string Alias { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;
}

public sealed class AppSettings
{
    /// <summary>界面语言。</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>识别语言：auto（自动检测）/ zh / en，独立于界面语言。</summary>
    public string RecognitionLanguage { get; set; } = "auto";

    public string Theme { get; set; } = "light";

    /// <summary>是否已完成「默认日间」迁移。旧版本默认夜间，首次加载时把遗留的 dark 迁移为 light。</summary>
    public bool ThemeMigrated { get; set; }

    public bool AutoStart { get; set; }

    /// <summary>启动时是否静默检查更新（可在设置中关闭，保持完全离线）。</summary>
    public bool AutoCheckUpdate { get; set; } = true;

    /// <summary>唤醒快捷键文本，例如 "Ctrl+`"。空表示未设置。</summary>
    public string KeyboardShortcut { get; set; } = "Ctrl+Win";

    /// <summary>hold = 按住说话松开识别；toggle = 按一下开始、再按/静音结束。</summary>
    public string KeyboardMode { get; set; } = "hold";

    public string MicDeviceId { get; set; } = string.Empty;

    /// <summary>最长录音时长（秒）。0 或负数表示不限时长（仅靠静音超时/松开按键结束）。</summary>
    public int MaxRecordSeconds { get; set; } = 60;

    public int SilenceStopMs { get; set; } = 1200;

    public int VadSensitivity { get; set; } = 1;

    /// <summary>环境模式：auto（自动，动态自适应）/ quiet（安静，低阈值）/ noisy（嘈杂，高阈值）。</summary>
    public string NoiseEnvironment { get; set; } = "auto";

    /// <summary>输入增益（dB），补偿降噪麦克风输出电平偏低。0~18，默认 0（不增益）。</summary>
    public int InputGainDb { get; set; }

    /// <summary>无输入框时的动作，固定 clipboard。</summary>
    public string NoInputAction { get; set; } = "clipboard";

    /// <summary>输出方式：sendinput（直接键入，默认）/ clipboard（复制后自动粘贴）/ paste（粘贴）。</summary>
    public string OutputMethod { get; set; } = "sendinput";

    public bool DictionaryEnabled { get; set; } = true;

    public List<DictionaryEntry> Dictionary { get; set; } = new();

    public string ModelDirectory { get; set; } = string.Empty;

    public string ModelQuant { get; set; } = "Q8_0";

    /// <summary>0 表示动态端口。</summary>
    public int ServerPort { get; set; }

    public bool WarmupAtStartup { get; set; }

    /// <summary>gpu / cpu / auto。</summary>
    public string InferenceDevice { get; set; } = "gpu";

    public string PromptPrefix { get; set; } = "参考词汇：";

    [JsonIgnore]
    public bool IsEnglish => Language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Language = Language,
            RecognitionLanguage = RecognitionLanguage,
            Theme = Theme,
            ThemeMigrated = ThemeMigrated,
            AutoStart = AutoStart,
            AutoCheckUpdate = AutoCheckUpdate,
            KeyboardShortcut = KeyboardShortcut,
            KeyboardMode = KeyboardMode,
            MicDeviceId = MicDeviceId,
            MaxRecordSeconds = MaxRecordSeconds,
            SilenceStopMs = SilenceStopMs,
            VadSensitivity = VadSensitivity,
            NoiseEnvironment = NoiseEnvironment,
            InputGainDb = InputGainDb,
            NoInputAction = NoInputAction,
            OutputMethod = OutputMethod,
            DictionaryEnabled = DictionaryEnabled,
            Dictionary = Dictionary.Select(e => new DictionaryEntry { Alias = e.Alias, Target = e.Target }).ToList(),
            ModelDirectory = ModelDirectory,
            ModelQuant = ModelQuant,
            ServerPort = ServerPort,
            WarmupAtStartup = WarmupAtStartup,
            InferenceDevice = InferenceDevice,
            PromptPrefix = PromptPrefix
        };
    }
}
