using Xunit;
using BaiYunGe.Core.Keyboard;

namespace BaiYunGe.Tests.Core;

public class HotkeyDefinitionTests
{
    [Theory]
    [InlineData("Ctrl+`", true, false, false, false, 0xC0)]
    [InlineData("Ctrl+Shift+K", true, false, true, false, 0x4B)]
    [InlineData("Alt+F9", false, true, false, false, 0x78)]
    [InlineData("F12", false, false, false, false, 0x7B)]
    [InlineData("Win+Space", false, false, false, true, 0x20)]
    public void TryParse_Valid(string text, bool ctrl, bool alt, bool shift, bool win, int key)
    {
        Assert.True(HotkeyDefinition.TryParse(text, out var definition));
        Assert.Equal(ctrl, definition.Ctrl);
        Assert.Equal(alt, definition.Alt);
        Assert.Equal(shift, definition.Shift);
        Assert.Equal(win, definition.Win);
        Assert.Equal(key, definition.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("InvalidKey")]
    public void TryParse_Invalid_ReturnsFalse(string text)
    {
        Assert.False(HotkeyDefinition.TryParse(text, out _));
    }

    [Fact]
    public void DisplayText_Normalizes()
    {
        Assert.True(HotkeyDefinition.TryParse("ctrl+`", out var definition));
        Assert.Equal("Ctrl+`", definition.DisplayText);
    }

    [Theory]
    [InlineData("Ctrl+Win")]
    [InlineData("Win")]
    [InlineData("Ctrl+Shift+Win")]
    [InlineData("Win+E")]
    [InlineData("Ctrl+`")]
    public void DisplayText_RoundTrips(string text)
    {
        // 录制→DisplayText→持久化→重新解析 必须无损往返（修复 Win 主键 round-trip 缺陷）。
        Assert.True(HotkeyDefinition.TryParse(text, out var first));
        Assert.True(HotkeyDefinition.TryParse(first.DisplayText, out var second));
        Assert.Equal(first.Ctrl, second.Ctrl);
        Assert.Equal(first.Alt, second.Alt);
        Assert.Equal(first.Shift, second.Shift);
        Assert.Equal(first.Win, second.Win);
        Assert.Equal(first.Key, second.Key);
    }
}
