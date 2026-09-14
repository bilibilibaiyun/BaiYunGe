using Xunit;
using BaiYunGe.Core;

namespace BaiYunGe.Tests.Core;

public class AsrResponseParserTests
{
    private readonly AsrResponseParser _parser = new();

    [Fact]
    public void Parse_WithMarker_ExtractsText()
    {
        var result = _parser.Parse("language Chinese<asr_text>你好，白云。");
        Assert.Equal("你好，白云。", result.Text);
    }

    [Fact]
    public void Parse_WithoutMarker_ReturnsEmptyText()
    {
        var result = _parser.Parse("some unexpected response");
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal("some unexpected response", result.Raw);
    }

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        var result = _parser.Parse(string.Empty);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public void Parse_TrimsWhitespace()
    {
        var result = _parser.Parse("language Chinese<asr_text>  文本  ");
        Assert.Equal("文本", result.Text);
    }
}
