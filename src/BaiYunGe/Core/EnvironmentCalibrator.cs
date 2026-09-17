using BaiYunGe.Core.Audio;
using NAudio.Wave;

namespace BaiYunGe.Core;

/// <summary>
/// 环境采样：分析一段用户录制的基准音频（放松说话），自动推断适合当前环境的语音检测
/// 阈值。原理：分段（30ms）计算 RMS，同时用频谱指纹（语音频段能量占比）区分「人声帧」
/// 与「噪声帧」，噪声地板取噪声帧 RMS 低分位、语音峰值取人声帧 RMS 高分位，阈值取两者
/// 之间略偏向噪声地板处——既灵敏又不易把环境噪声误判为人声。
/// </summary>
public static class EnvironmentCalibrator
{
    private const int SampleRate = 16000;
    private const int FrameSize = 480;      // 30ms = 480 采样
    private const int FftSize = 512;        // 32ms = 512 采样
    private const double VoiceRatioThreshold = 0.40;
    private const double NoisePercentile = 0.15;
    private const double SpeechPercentile = 0.90;
    private const double ThresholdBias = 0.45;

    /// <summary>分析 WAV 文件（16kHz/16bit/mono），返回建议的检测阈值（dB）。返回 0 表示无法分析。</summary>
    public static double AnalyzeThreshold(string wavPath)
    {
        var samples = ReadSamples(wavPath);
        if (samples.Length < FftSize)
        {
            return 0;
        }

        var speechRmsDb = new List<double>();
        var noiseRmsDb = new List<double>();

        for (var i = 0; i + FrameSize <= samples.Length; i += FrameSize)
        {
            var rmsDb = ComputeRmsDb(samples, i, FrameSize);

            // 频谱指纹：取 512 采样窗口（不足补零），算语音频段能量占比，区分人声帧与噪声帧。
            var frame = new float[FftSize];
            var count = Math.Min(FftSize, samples.Length - i);
            Array.Copy(samples, i, frame, 0, count);
            var voiceRatio = SpectrumAnalyzer.ComputeVoiceRatio(frame);

            if (voiceRatio >= VoiceRatioThreshold)
            {
                speechRmsDb.Add(rmsDb);
            }
            else
            {
                noiseRmsDb.Add(rmsDb);
            }
        }

        if (noiseRmsDb.Count < 5 || speechRmsDb.Count < 5)
        {
            return 0;
        }

        noiseRmsDb.Sort();
        speechRmsDb.Sort();
        var noiseFloor = noiseRmsDb[(int)(noiseRmsDb.Count * NoisePercentile)];
        var speechPeak = speechRmsDb[(int)(speechRmsDb.Count * SpeechPercentile)];

        // 阈值取噪声地板与语音峰值之间，略偏向噪声地板，更灵敏（能检测轻声）。
        // 不再设「人声与噪声最小差值」门槛：小声说话时语音峰值可能只略高于噪声地板，
        // 设门槛会误判采样失败。是否真的有人声已由频谱指纹（speechRmsDb 非空）保证。
        return noiseFloor + (speechPeak - noiseFloor) * ThresholdBias;
    }

    private static float[] ReadSamples(string wavPath)
    {
        using var reader = new WaveFileReader(wavPath);
        var bytes = new byte[reader.Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var n = reader.Read(bytes, read, bytes.Length - read);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        var sampleCount = read / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
        }

        return samples;
    }

    private static double ComputeRmsDb(float[] samples, int offset, int count)
    {
        double sum = 0;
        for (var i = offset; i < offset + count; i++)
        {
            sum += samples[i] * samples[i];
        }

        var rms = Math.Sqrt(sum / count);
        return 20 * Math.Log10(Math.Max(rms, 1e-9));
    }
}
