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

        // 先有语音。
        vad.AddFrame(-30, 0.0, 0.1);
        Assert.True(vad.HasUsableSpeech());

        // 连续静音累计 500ms 触发超时。
        for (var i = 0; i < 5; i++)
        {
            vad.AddFrame(-60, 0.1 + i * 0.1, 0.1);
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

        vad.AddFrame(-30, 0.0, 0.1);
        vad.AddFrame(-60, 0.1, 0.2); // 200ms 静音，未达 500ms
        vad.AddFrame(-30, 0.3, 0.1); // 又有语音，重置

        Assert.Equal(0, timedOut);
    }
}
