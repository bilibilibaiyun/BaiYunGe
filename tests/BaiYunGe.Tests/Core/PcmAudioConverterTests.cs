using Xunit;
using NAudio.Wave;
using BaiYunGe.Core.Audio;

namespace BaiYunGe.Tests.Core;

public class PcmAudioConverterTests
{
    private readonly PcmAudioConverter _converter = new();

    [Fact]
    public void ToMonoFloat_16BitMono_AveragesCorrectly()
    {
        // 16bit 单声道：一个采样 = 0x7FFF（正满幅）。
        var buffer = new byte[] { 0xFF, 0x7F };
        var format = new WaveFormat(48000, 16, 1);
        var result = _converter.ToMonoFloat(buffer, buffer.Length, format);
        Assert.Single(result);
        Assert.InRange(result[0], 0.99f, 1.01f);
    }

    [Fact]
    public void ToMonoFloat_Stereo_AveragesChannels()
    {
        // 16bit 双声道：左=0x7FFF（正满幅），右=0 → 平均 0.5。
        var buffer = new byte[] { 0xFF, 0x7F, 0x00, 0x00 };
        var format = new WaveFormat(48000, 16, 2);
        var result = _converter.ToMonoFloat(buffer, buffer.Length, format);
        Assert.Single(result);
        Assert.InRange(result[0], 0.49f, 0.51f);
    }

    [Fact]
    public void Resample_Downsample_PreservesLengthRatio()
    {
        var input = new float[48000];
        var result = _converter.Resample(input, 48000);
        Assert.Equal(16000, result.Length);
    }

    [Fact]
    public void Resample_AlreadyTarget_ReturnsSame()
    {
        var input = new float[100];
        var result = _converter.Resample(input, 16000);
        Assert.Same(input, result);
    }

    [Fact]
    public void ToPcm16_ClampsToShortRange()
    {
        var samples = new[] { 1.5f, -1.5f, 0f };
        var bytes = _converter.ToPcm16(samples);
        Assert.Equal(6, bytes.Length);
    }
}
