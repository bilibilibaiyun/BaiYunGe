using System.Text;

namespace BaiYunGe.Core.Keyboard;

/// <summary>快捷键定义：一组修饰键 + 一个主键，支持文本解析与规范化。</summary>
public sealed record HotkeyDefinition
{
    public bool Ctrl { get; init; }

    public bool Alt { get; init; }

    public bool Shift { get; init; }

    public bool Win { get; init; }

    /// <summary>主键虚拟键码，0 表示未设置。</summary>
    public int Key { get; init; }

    public bool IsEmpty => Key == 0;

    /// <summary>规范化显示文本，如 "Ctrl+`"。</summary>
    public string DisplayText => ToDisplayText();

    public static HotkeyDefinition Empty => new();

    public static bool TryParse(string? text, out HotkeyDefinition definition)
    {
        definition = Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var ctrl = false;
        var alt = false;
        var shift = false;
        var win = false;
        var key = 0;

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    continue;
                case "alt":
                    alt = true;
                    continue;
                case "shift":
                    shift = true;
                    continue;
                case "win":
                case "windows":
                case "meta":
                    win = true;
                    continue;
            }

            if (TryParseKey(part, out var parsedKey))
            {
                key = parsedKey;
                continue;
            }

            return false;
        }

        if (key == 0)
        {
            return false;
        }

        definition = new HotkeyDefinition
        {
            Ctrl = ctrl,
            Alt = alt,
            Shift = shift,
            Win = win,
            Key = key
        };
        return true;
    }

    public bool Matches(bool ctrl, bool alt, bool shift, bool win, int key)
    {
        return Ctrl == ctrl && Alt == alt && Shift == shift && Win == win && Key == key;
    }

    private string ToDisplayText()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        if (Ctrl) builder.Append("Ctrl+");
        if (Alt) builder.Append("Alt+");
        if (Shift) builder.Append("Shift+");
        if (Win) builder.Append("Win+");
        builder.Append(KeyName(Key));
        return builder.ToString();
    }

    public static string KeyName(int key)
    {
        return key switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            >= 0x30 and <= 0x39 => ((char)key).ToString(),
            >= 0x41 and <= 0x5A => ((char)key).ToString(),
            >= 0x60 and <= 0x69 => "Num" + (key - 0x60),
            0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4",
            0x74 => "F5", 0x75 => "F6", 0x76 => "F7", 0x77 => "F8",
            0x78 => "F9", 0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => $"VK{key}"
        };
    }

    private static bool TryParseKey(string part, out int key)
    {
        key = 0;
        if (part.Length == 1)
        {
            var ch = char.ToUpperInvariant(part[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                key = ch;
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                key = ch;
                return true;
            }
        }

        key = part.ToUpperInvariant() switch
        {
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ENTER" => 0x0D,
            "ESC" => 0x1B,
            "`" => 0xC0,
            "~" => 0xC0,
            "-" => 0xBD,
            "=" => 0xBB,
            "[" => 0xDB,
            "]" => 0xDD,
            "\\" => 0xDC,
            ";" => 0xBA,
            "'" => 0xDE,
            "," => 0xBC,
            "." => 0xBE,
            "/" => 0xBF,
            _ => 0
        };

        return key != 0;
    }
}
