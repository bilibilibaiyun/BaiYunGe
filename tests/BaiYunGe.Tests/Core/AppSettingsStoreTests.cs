using Xunit;
using BaiYunGe.Core;

namespace BaiYunGe.Tests.Core;

public class AppSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"baiyunge-test-{Guid.NewGuid():N}");
    private readonly string _configPath;
    private readonly AppLogger _logger;
    private readonly AppSettingsStore _store;

    public AppSettingsStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _configPath = Path.Combine(_dir, "config.json");
        _logger = new AppLogger(Path.Combine(_dir, "logs"));
        _store = new AppSettingsStore(_logger, _configPath);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = _store.Load();
        Assert.Equal("zh-CN", settings.Language);
        Assert.Empty(settings.Dictionary);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var settings = new AppSettings
        {
            Language = "en-US",
            KeyboardShortcut = "Ctrl+`",
            Dictionary = { new DictionaryEntry { Alias = "白云", Target = "白云先生" } }
        };

        _store.Save(settings);
        var loaded = _store.Load();

        Assert.Equal("en-US", loaded.Language);
        Assert.Equal("Ctrl+`", loaded.KeyboardShortcut);
        Assert.Single(loaded.Dictionary);
        Assert.Equal("白云先生", loaded.Dictionary[0].Target);
    }

    [Fact]
    public void Load_CorruptedJson_ReturnsDefaultsWithoutDeleting()
    {
        File.WriteAllText(_configPath, "{ this is not valid json");

        var settings = _store.Load();

        Assert.Equal("zh-CN", settings.Language);
        Assert.True(File.Exists(_configPath), "损坏的配置不应被静默删除");
    }

    public void Dispose()
    {
        _logger.Dispose();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
        }
    }
}
