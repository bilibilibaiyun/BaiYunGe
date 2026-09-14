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
}
