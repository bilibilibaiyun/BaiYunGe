using NAudio.Wave;

namespace BaiYunGe.Core.Audio;

/// <summary>
/// 把任意 WASAPI 设备格式统一转换为 16kHz / 16bit / 单声道 PCM。
/// 流程：任意格式字节 → float 单声道（去交织）→ 线性重采样到 16kHz → PCM16 字节。
/// </summary>
public sealed class PcmAudioConverter
{
    public const int TargetSampleRate = 16000;

    private static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>设备原始帧 → float 单声道。</summary>
    public float[] ToMonoFloat(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (bytesRecorded <= 0 || buffer.Length == 0)
        {
            return Array.Empty<float>();
        }

        var blockAlign = Math.Max(1, format.BlockAlign);
        var frames = bytesRecorded / blockAlign;
        if (frames <= 0)
        {
            return Array.Empty<float>();
        }

        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        var isFloat = IsFloat(format);
        var output = new float[frames];

        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0d;
            var baseOffset = frame * blockAlign;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += ReadSample(buffer, baseOffset + channel * bytesPerSample, bytesPerSample, isFloat);
            }

            output[frame] = (float)(sum / channels);
        }

        return output;
    }

    /// <summary>线性插值重采样到 16kHz。</summary>
    public float[] Resample(float[] input, int inputSampleRate)
    {
        if (input.Length == 0 || inputSampleRate == TargetSampleRate)
        {
            return input;
        }

        var outputLength = Math.Max(1, (int)Math.Ceiling(input.Length * (double)TargetSampleRate / inputSampleRate));
        var output = new float[outputLength];
        var step = inputSampleRate / (double)TargetSampleRate;

        for (var i = 0; i < outputLength; i++)
        {
            var position = i * step;
            var left = Math.Min(input.Length - 1, (int)Math.Floor(position));
            var right = Math.Min(input.Length - 1, left + 1);
            var fraction = position - left;
            output[i] = input[left] * (1f - (float)fraction) + input[right] * (float)fraction;
        }

        return output;
    }

    /// <summary>float 采样 → 16bit PCM 字节（小端）。</summary>
    public byte[] ToPcm16(float[] samples)
    {
        if (samples.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var output = new byte[samples.Length * sizeof(short)];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = Math.Clamp(samples[i], -1f, 1f);
            short sample = value <= -1f
                ? short.MinValue
                : value >= 1f
                    ? short.MaxValue
                    : (short)Math.Round(value * short.MaxValue, MidpointRounding.AwayFromZero);

            output[i * 2] = (byte)sample;
            output[i * 2 + 1] = (byte)(sample >> 8);
        }

        return output;
    }

    private static bool IsFloat(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return true;
        }

        return format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubtype;
    }

    private static float ReadSample(byte[] buffer, int offset, int bytesPerSample, bool isFloat)
    {
        if (isFloat)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        return bytesPerSample switch
        {
            1 => (sbyte)buffer[offset] / 128f,
            2 => BitConverter.ToInt16(buffer, offset) / 32768f,
            3 => ReadInt24(buffer, offset) / 8388608f,
            4 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => 0f
        };
    }

    private static int ReadInt24(byte[] buffer, int offset)
    {
        var value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }
}
