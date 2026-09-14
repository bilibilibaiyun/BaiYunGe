using System.Text.Json.Serialization;

namespace BaiYunGe.Core;

public sealed record DictionaryEntry
{
    public string Alias { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;
}

public sealed class AppSettings
{
    public string Language { get; set; } = "zh-CN";

    public string Theme { get; set; } = "dark";

    public bool AutoStart { get; set; }

    /// <summary>唤醒快捷键文本，例如 "Ctrl+`"。空表示未设置。</summary>
    public string KeyboardShortcut { get; set; } = string.Empty;

    /// <summary>hold = 按住说话松开识别；toggle = 按一下开始、再按/静音结束。</summary>
    public string KeyboardMode { get; set; } = "hold";

    public string MicDeviceId { get; set; } = string.Empty;

    /// <summary>最长录音时长（秒）。0 或负数表示不限时长（仅靠静音超时/松开按键结束）。</summary>
    public int MaxRecordSeconds { get; set; }

    public int SilenceStopMs { get; set; } = 1200;

    public int VadSensitivity { get; set; } = 1;

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
            Theme = Theme,
            AutoStart = AutoStart,
            KeyboardShortcut = KeyboardShortcut,
            KeyboardMode = KeyboardMode,
            MicDeviceId = MicDeviceId,
            MaxRecordSeconds = MaxRecordSeconds,
            SilenceStopMs = SilenceStopMs,
            VadSensitivity = VadSensitivity,
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
