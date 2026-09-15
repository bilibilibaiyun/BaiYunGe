using System.Text;

namespace BaiYunGe.Core;

/// <summary>词典：别名自动替换 + 作为 prompt 参考词汇提供给模型。</summary>
public sealed class DictionaryProcessor
{
    /// <summary>构建给模型的参考词汇 prompt（同时包含别名与目标词，去重）。</summary>
    public string BuildPrompt(IReadOnlyList<DictionaryEntry> dictionary, bool enabled)
    {
        if (!enabled || dictionary.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in dictionary)
        {
            // 别名和目标词都作为参考词汇，帮助模型把音近/别字纠正为词典词。
            foreach (var raw in new[] { entry.Alias, entry.Target })
            {
                var word = raw?.Trim();
                if (string.IsNullOrWhiteSpace(word) || !seen.Add(word))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('、');
                }

                builder.Append(word);
            }
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
