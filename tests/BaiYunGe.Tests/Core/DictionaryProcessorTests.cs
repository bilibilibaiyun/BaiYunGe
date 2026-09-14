using Xunit;
using BaiYunGe.Core;

namespace BaiYunGe.Tests.Core;

public class DictionaryProcessorTests
{
    private readonly DictionaryProcessor _processor = new();

    [Fact]
    public void BuildPrompt_ListsTargets()
    {
        var dict = new List<DictionaryEntry>
        {
            new() { Alias = "白云", Target = "白云先生" },
            new() { Alias = "ASR", Target = "语音识别" }
        };

        var prompt = _processor.BuildPrompt(dict, true);
        Assert.Equal("白云先生、语音识别", prompt);
    }

    [Fact]
    public void BuildPrompt_Disabled_ReturnsEmpty()
    {
        var dict = new List<DictionaryEntry> { new() { Alias = "白云", Target = "白云先生" } };
        Assert.Equal(string.Empty, _processor.BuildPrompt(dict, false));
    }

    [Fact]
    public void ReplaceAliases_ReplacesAliasWithTarget()
    {
        var dict = new List<DictionaryEntry> { new() { Alias = "白云", Target = "白云先生" } };
        var result = _processor.ReplaceAliases("你好白云", dict, true);
        Assert.Equal("你好白云先生", result);
    }

    [Fact]
    public void ReplaceAliases_LongerAliasFirst()
    {
        var dict = new List<DictionaryEntry>
        {
            new() { Alias = "白云", Target = "A" },
            new() { Alias = "白云先生", Target = "B" }
        };

        var result = _processor.ReplaceAliases("我是白云先生", dict, true);
        Assert.Equal("我是B", result);
    }

    [Fact]
    public void ReplaceAliases_Disabled_ReturnsOriginal()
    {
        var dict = new List<DictionaryEntry> { new() { Alias = "白云", Target = "白云先生" } };
        var result = _processor.ReplaceAliases("你好白云", dict, false);
        Assert.Equal("你好白云", result);
    }
}
