using Xunit;
using BaiYunGe.Core;

namespace BaiYunGe.Tests.Core;

public class AppPathsTests
{
    [Fact]
    public void OverrideDataDir_IsUsed()
    {
        var paths = new AppPaths("D:\\TestData");
        Assert.Equal(Path.Combine("D:\\TestData", "logs"), paths.Logs);
        Assert.Equal(Path.Combine("D:\\TestData", "temp"), paths.Temp);
        Assert.Equal(Path.Combine("D:\\TestData", "config.json"), paths.ConfigFile);
    }

    [Fact]
    public void CreateTempWavPath_IsUnique()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), $"baiyunge-{Guid.NewGuid():N}"));
        var first = paths.CreateTempWavPath();
        var second = paths.CreateTempWavPath();
        Assert.NotEqual(first, second);
        Assert.EndsWith(".wav", first);
    }
}
