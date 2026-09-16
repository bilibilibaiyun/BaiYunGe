using Xunit;
using BaiYunGe.Core.Audio;

namespace BaiYunGe.Tests.Core;

public class VoiceActivityDetectorTests
{
    [Fact]
    public void Speech_ThenSilence_TriggersTimeout()
    {
        var vad = new VoiceActivityDetector(1, 500);
        var timedOut = 0;
        vad.SilenceTimeout += (_, _) => timedOut++;

        // 先有语音（连续 3 帧超过阈值才判语音）。
        vad.AddFrame(-30, 0.0, 0.1);
        vad.AddFrame(-30, 0.1, 0.1);
        vad.AddFrame(-30, 0.2, 0.1);
        Assert.True(vad.HasUsableSpeech());

        // 连续静音累计 500ms 触发超时。
        for (var i = 0; i < 5; i++)
        {
            vad.AddFrame(-60, 0.3 + i * 0.1, 0.1);
        }

        Assert.Equal(1, timedOut);
    }

    [Fact]
    public void SilenceOnly_NoUsableSpeech()
    {
        var vad = new VoiceActivityDetector(1, 500);
        for (var i = 0; i < 10; i++)
        {
            vad.AddFrame(-60, i * 0.1, 0.1);
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
        vad.AddFrame(-30, 0.0, 0.1);
        vad.AddFrame(-30, 0.1, 0.1);
        vad.AddFrame(-30, 0.2, 0.1);
        vad.AddFrame(-60, 0.3, 0.2); // 200ms 静音，未达 500ms
        // 又连续 3 帧语音，重置静音累计。
        vad.AddFrame(-30, 0.5, 0.1);
        vad.AddFrame(-30, 0.6, 0.1);
        vad.AddFrame(-30, 0.7, 0.1);

        Assert.Equal(0, timedOut);
    }
}
