namespace BaiYunGe.Core.Audio;

/// <summary>
/// 频谱分析：计算语音频段（约 187~3406Hz）能量占全频段的比例，作为区分人声与环境
/// 噪声的「人声指纹」。人声能量集中在语音频段（占比高），风扇等持续低频噪声集中在
/// &lt;187Hz（占比低），白噪声各频段均匀（占比约 38%）。
/// </summary>
internal static class SpectrumAnalyzer
{
    private const int FftSize = 512;
    private const int VoiceBandStartBin = 6;   // 约 187Hz
    private const int VoiceBandEndBin = 109;   // 约 3406Hz

    /// <summary>计算 512 点采样的语音频段能量占比（0~1）。人声集中在语音频段，占比高。</summary>
    public static double ComputeVoiceRatio(float[] samples)
    {
        if (samples is null || samples.Length == 0)
        {
            return 0;
        }

        var real = new double[FftSize];
        var imag = new double[FftSize];
        var count = Math.Min(samples.Length, FftSize);
        for (var i = 0; i < count; i++)
        {
            var window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (count - 1)));
            real[i] = samples[i] * window;
        }

        Fft(real, imag);

        double voiceEnergy = 0;
        double totalEnergy = 0;
        for (var i = 1; i < FftSize / 2; i++)
        {
            var energy = real[i] * real[i] + imag[i] * imag[i];
            totalEnergy += energy;
            if (i >= VoiceBandStartBin && i <= VoiceBandEndBin)
            {
                voiceEnergy += energy;
            }
        }

        return totalEnergy <= 0 ? 0 : voiceEnergy / totalEnergy;
    }

    /// <summary>原地 Radix-2 Cooley-Tukey FFT（real/imag 为输入输出）。</summary>
    internal static void Fft(double[] real, double[] imag)
    {
        var n = real.Length;

        // 位反转重排。
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        // 蝶形运算。
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2.0 * Math.PI / len;
            var wLenReal = Math.Cos(angle);
            var wLenImag = Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                var wReal = 1.0;
                var wImag = 0.0;
                for (var k = 0; k < len / 2; k++)
                {
                    var uReal = real[i + k];
                    var uImag = imag[i + k];
                    var vReal = real[i + k + len / 2] * wReal - imag[i + k + len / 2] * wImag;
                    var vImag = real[i + k + len / 2] * wImag + imag[i + k + len / 2] * wReal;
                    real[i + k] = uReal + vReal;
                    imag[i + k] = uImag + vImag;
                    real[i + k + len / 2] = uReal - vReal;
                    imag[i + k + len / 2] = uImag - vImag;

                    var nextWReal = wReal * wLenReal - wImag * wLenImag;
                    var nextWImag = wReal * wLenImag + wImag * wLenReal;
                    wReal = nextWReal;
                    wImag = nextWImag;
                }
            }
        }
    }
}
