using Xunit;
using BaiYunGe.Core.Audio;

namespace BaiYunGe.Tests.Core;

public class VoiceActivityDetectorTests
{
    private const int SampleRate = 16000;

    /// <summary>生成 440Hz 正弦波采样（能量集中在语音频段 300~3400Hz，模拟人声）。</summary>
    private static float[] VoiceSamples(double amplitude = 0.5)
    {
        var samples = new float[160]; // 10ms @ 16kHz
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * 440 * i / SampleRate));
        }

        return samples;
    }

    /// <summary>静音采样（全 0，语音频段能量占比为 0）。</summary>
    private static float[] SilenceSamples()
    {
        return new float[160];
    }

    [Fact]
    public void Speech_ThenSilence_TriggersTimeout()
    {
        var vad = new VoiceActivityDetector(1, 500);
        var timedOut = 0;
        vad.SilenceTimeout += (_, _) => timedOut++;

        // 先有语音（连续 3 帧判语音）。
        vad.AddFrame(VoiceSamples(), -30, 0.0, 0.1);
        vad.AddFrame(VoiceSamples(), -30, 0.1, 0.1);
        vad.AddFrame(VoiceSamples(), -30, 0.2, 0.1);
        Assert.True(vad.HasUsableSpeech());

        // 连续静音累计 500ms 触发超时。
        for (var i = 0; i < 5; i++)
        {
            vad.AddFrame(SilenceSamples(), -60, 0.3 + i * 0.1, 0.1);
        }

        Assert.Equal(1, timedOut);
    }

    [Fact]
    public void SilenceOnly_NoUsableSpeech()
    {
        var vad = new VoiceActivityDetector(1, 500);
        for (var i = 0; i < 10; i++)
        {
            vad.AddFrame(SilenceSamples(), -60, i * 0.1, 0.1);
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
        vad.AddFrame(VoiceSamples(), -30, 0.0, 0.1);
        vad.AddFrame(VoiceSamples(), -30, 0.1, 0.1);
        vad.AddFrame(VoiceSamples(), -30, 0.2, 0.1);
        vad.AddFrame(SilenceSamples(), -60, 0.3, 0.2); // 200ms 静音，未达 500ms
        // 又连续 3 帧语音，重置静音累计。
        vad.AddFrame(VoiceSamples(), -30, 0.5, 0.1);
        vad.AddFrame(VoiceSamples(), -30, 0.6, 0.1);
        vad.AddFrame(VoiceSamples(), -30, 0.7, 0.1);

        Assert.Equal(0, timedOut);
    }
}
