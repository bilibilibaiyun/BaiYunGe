using System.Text.Json;

namespace BaiYunGe.Core;

/// <summary>
/// 配置读写。损坏时写明确错误并重建默认，绝不静默清空用户数据。
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppLogger _logger;
    private readonly string _configPath;
    private readonly object _sync = new();

    public AppSettingsStore(AppLogger logger, string configPath)
    {
        _logger = logger;
        _configPath = configPath;
    }

    public AppSettings Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_configPath))
            {
                var defaults = new AppSettings();
                _logger.Info("Config file not found; using defaults.");
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                settings.Dictionary ??= new List<DictionaryEntry>();

                // 旧版本默认夜间：首次加载时把遗留的 dark 迁移为 light（用户要求默认日间），
                // 迁移后打标记，避免覆盖用户之后主动选择的夜间。
                if (!settings.ThemeMigrated &&
                    string.Equals(settings.Theme, "dark", StringComparison.OrdinalIgnoreCase))
                {
                    settings.Theme = "light";
                    settings.ThemeMigrated = true;
                    Save(settings);
                    _logger.Info("Theme default migrated from dark to light.");
                }

                return settings;
            }
            catch (Exception exception)
            {
                // 明确记录错误，但不静默删除用户文件；保留原文件供排查。
                _logger.Error($"Failed to load config from {_configPath}; using defaults.", exception);
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_sync)
        {
            try
            {
                var directory = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(settings, JsonOptions);
                var tmp = _configPath + ".tmp";
                File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                File.Move(tmp, _configPath, overwrite: true);
            }
            catch (Exception exception)
            {
                _logger.Error($"Failed to save config to {_configPath}.", exception);
            }
        }
    }
}
