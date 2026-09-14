namespace BaiYunGe.Bootstrapper;

public sealed class InstallerText
{
    private readonly Dictionary<string, string> _zh = new(StringComparer.Ordinal)
    {
        ["Title"] = "白云歌安装程序",
        ["AppName"] = "白云歌",
        ["Language"] = "语言",
        ["Chinese"] = "简体中文",
        ["English"] = "English",
        ["ReadyInstall"] = "选择安装语言，然后开始安装。",
        ["ReadyModify"] = "选择要执行的操作。",
        ["ReadyUpdate"] = "发现已安装的旧版本，可以立即更新。",
        ["Install"] = "安装",
        ["Update"] = "更新",
        ["Repair"] = "修复",
        ["Uninstall"] = "卸载",
        ["Cancel"] = "取消",
        ["Close"] = "关闭",
        ["Detecting"] = "正在检查安装状态...",
        ["Preparing"] = "正在准备安装...",
        ["Installing"] = "正在安装白云歌...",
        ["Repairing"] = "正在修复白云歌...",
        ["Uninstalling"] = "正在卸载白云歌...",
        ["Complete"] = "操作已完成。",
        ["Failed"] = "操作失败。",
        ["RestartRequired"] = "需要重启计算机才能完成操作。"
    };

    private readonly Dictionary<string, string> _en = new(StringComparer.Ordinal)
    {
        ["Title"] = "BaiYunGe Setup",
        ["AppName"] = "BaiYunGe",
        ["Language"] = "Language",
        ["Chinese"] = "简体中文",
        ["English"] = "English",
        ["ReadyInstall"] = "Choose the installation language, then start the installation.",
        ["ReadyModify"] = "Choose the operation to perform.",
        ["ReadyUpdate"] = "An older version is installed and can be updated now.",
        ["Install"] = "Install",
        ["Update"] = "Update",
        ["Repair"] = "Repair",
        ["Uninstall"] = "Uninstall",
        ["Cancel"] = "Cancel",
        ["Close"] = "Close",
        ["Detecting"] = "Checking installation status...",
        ["Preparing"] = "Preparing installation...",
        ["Installing"] = "Installing BaiYunGe...",
        ["Repairing"] = "Repairing BaiYunGe...",
        ["Uninstalling"] = "Uninstalling BaiYunGe...",
        ["Complete"] = "The operation completed successfully.",
        ["Failed"] = "The operation failed.",
        ["RestartRequired"] = "Restart the computer to complete the operation."
    };

    private string _language;

    public InstallerText(string language)
    {
        _language = NormalizeLanguage(language);
    }

    public string CurrentLanguage => _language;

    public string Get(string key)
    {
        var table = _language.Equals("en-US", StringComparison.Ordinal)
            ? _en
            : _zh;
        return table.TryGetValue(key, out var value) ? value : key;
    }

    public void ChangeLanguage(string language)
    {
        _language = NormalizeLanguage(language);
    }

    public static string NormalizeLanguage(string? language)
    {
        var value = language?.Trim();
        return value?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true
            ? "en-US"
            : "zh-CN";
    }

    public static string FromOperatingSystem()
    {
        return System.Globalization.CultureInfo.CurrentUICulture
            .TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en-US";
    }
}
