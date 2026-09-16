using Xunit;
using BaiYunGe.Core.Audio;

namespace BaiYunGe.Tests.Core;

public class VoiceActivityDetectorTests
{
    private const int SampleRate = 16000;
    private const int FrameSamples = 512; // 32ms @ 16kHz，与 VAD 的 FFT 窗口一致
    private const double FrameSeconds = FrameSamples / (double)SampleRate;

    /// <summary>生成 440Hz 正弦波采样（能量集中在语音频段 300~3400Hz，模拟人声）。</summary>
    private static float[] VoiceSamples(double amplitude = 0.5)
    {
        var samples = new float[FrameSamples];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * 440 * i / SampleRate));
        }

        return samples;
    }

    /// <summary>静音采样（全 0，语音频段能量占比为 0）。</summary>
    private static float[] SilenceSamples()
    {
        return new float[FrameSamples];
    }

    [Fact]
    public void Speech_ThenSilence_TriggersTimeout()
    {
        var vad = new VoiceActivityDetector(1, 500);
        var timedOut = 0;
        vad.SilenceTimeout += (_, _) => timedOut++;

        // 先有语音（连续 3 帧判语音）。
        vad.AddFrame(VoiceSamples(), -30, 0.0, FrameSeconds);
        vad.AddFrame(VoiceSamples(), -30, FrameSeconds, FrameSeconds);
        vad.AddFrame(VoiceSamples(), -30, FrameSeconds * 2, FrameSeconds);
        Assert.True(vad.HasUsableSpeech());

        // 连续静音累计 500ms 触发超时。
        for (var i = 0; i < 16; i++)
        {
            vad.AddFrame(SilenceSamples(), -60, FrameSeconds * 3 + i * FrameSeconds, FrameSeconds);
        }

        Assert.Equal(1, timedOut);
    }

    [Fact]
    public void SilenceOnly_NoUsableSpeech()
    {
        var vad = new VoiceActivityDetector(1, 500);
        for (var i = 0; i < 20; i++)
        {
            vad.AddFrame(SilenceSamples(), -60, i * FrameSeconds, FrameSeconds);
        }

        Assert.False(vad.HasUsableSpeech());
    }

    [Fact]
    public void IntermittentSpeech_DoesNotTimeout()
    {
        var vad = new VoiceActivityDetector(1, 500);
        var timedOut = 0;
        vad.SilenceTimeout += (_, _) => timedOut++;

        // 连续 3 帧语音。
        vad.AddFrame(VoiceSamples(), -30, 0.0, FrameSeconds);
        vad.AddFrame(VoiceSamples(), -30, FrameSeconds, FrameSeconds);
        vad.AddFrame(VoiceSamples(), -30, FrameSeconds * 2, FrameSeconds);
        // 3 帧静音（96ms），未达 500ms。
        vad.AddFrame(SilenceSamples(), -60, FrameSeconds * 3, FrameSeconds);
        vad.AddFrame(SilenceSamples(), -60, FrameSeconds * 4, FrameSeconds);
        vad.AddFrame(SilenceSamples(), -60, FrameSeconds * 5, FrameSeconds);
        // 又连续 3 帧语音，重置静音累计。
        vad.AddFrame(VoiceSamples(), -30, FrameSeconds * 6, FrameSeconds);
        vad.AddFrame(VoiceSamples(), -30, FrameSeconds * 7, FrameSeconds);
        vad.AddFrame(VoiceSamples(), -30, FrameSeconds * 8, FrameSeconds);

        Assert.Equal(0, timedOut);
    }
}
