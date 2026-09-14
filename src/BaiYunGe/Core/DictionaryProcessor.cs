using System.Text;

namespace BaiYunGe.Core;

/// <summary>词典：别名自动替换 + 作为 prompt 参考词汇提供给模型。</summary>
public sealed class DictionaryProcessor
{
    /// <summary>构建给模型的参考词汇 prompt。</summary>
    public string BuildPrompt(IReadOnlyList<DictionaryEntry> dictionary, bool enabled)
    {
        if (!enabled || dictionary.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var entry in dictionary)
        {
            if (string.IsNullOrWhiteSpace(entry.Target))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('、');
            }

            builder.Append(entry.Target.Trim());
        }

        return builder.ToString();
    }

    /// <summary>识别文本中命中别名则替换为目标词（先长后短，避免子串误替换）。</summary>
    public string ReplaceAliases(string text, IReadOnlyList<DictionaryEntry> dictionary, bool enabled)
    {
        if (!enabled || string.IsNullOrEmpty(text) || dictionary.Count == 0)
        {
            return text;
        }

        var result = text;
        foreach (var entry in dictionary.OrderByDescending(e => e.Alias.Length))
        {
            if (string.IsNullOrWhiteSpace(entry.Alias) || string.IsNullOrWhiteSpace(entry.Target))
            {
                continue;
            }

            result = result.Replace(entry.Alias, entry.Target, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
