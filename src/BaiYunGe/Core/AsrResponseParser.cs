namespace BaiYunGe.Core;

/// <summary>
/// 解析 llama-server ASR 响应，格式如
/// "language Chinese&lt;asr_text&gt;你好，我是哔哩哔哩白云。"。
/// 解析失败时不猜测输出，返回安全空文本并保留原始值。
/// </summary>
public sealed class AsrResponseParser
{
    private const string Marker = "<asr_text>";

    public ParsedAsr Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return new ParsedAsr(string.Empty, string.Empty);
        }

        var index = raw.IndexOf(Marker, StringComparison.Ordinal);
        if (index >= 0)
        {
            var text = raw[(index + Marker.Length)..].Trim();
            return new ParsedAsr(text, raw);
        }

        // 无标记：不猜测模型输出，返回空文本。
        return new ParsedAsr(string.Empty, raw);
    }
}

public sealed record ParsedAsr(string Text, string Raw);
