using System.Text;
using System.Text.Json;

namespace BaiYunGe.Core.Audio;

/// <summary>录音词典的 MFCC 模板存储：保存/加载词的声纹模板（JSON）。</summary>
public static class VoiceTemplateStore
{
    public static void Save(string path, double[][] mfcc)
    {
        var json = JsonSerializer.Serialize(mfcc);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public static double[][]? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<double[][]>(json);
        }
        catch
        {
            return null;
        }
    }
}
