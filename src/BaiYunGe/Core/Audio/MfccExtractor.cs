namespace BaiYunGe.Core.Audio;

/// <summary>
/// MFCC 特征提取与 DTW 动态时间规整比对，用于「录音词典」的声纹级词模板匹配：
/// 用户录音念词后提取 MFCC 序列作模板，识别时对疑似词典词提取 MFCC 并与模板比对，
/// 确认用户是否真的念了这个词（比文本级编辑距离更可靠，能同时缓解谐音与幻觉）。
/// </summary>
public static class MfccExtractor
{
    private const int NumMelFilters = 26;
    private const int NumCoeffs = 13;
    private const int FrameLength = 400;      // 25ms @ 16kHz
    private const int FrameShift = 160;       // 10ms @ 16kHz
    private const int FftSize = 512;

    /// <summary>读取 16kHz/16bit/mono WAV 为 float 采样（-1~1）。</summary>
    public static float[] ReadWav(string wavPath)
    {
        using var reader = new NAudio.Wave.WaveFileReader(wavPath);
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

    /// <summary>从 16kHz 单声道 PCM 采样提取 MFCC 序列（每帧 13 维）。</summary>
    public static double[][] Extract(float[] samples)
    {
        if (samples.Length < FrameLength)
        {
            return Array.Empty<double[]>();
        }

        var melFilters = BuildMelFilters();
        var frames = new List<double[]>();

        // 预加重（-0.97）。
        var pre = new double[samples.Length];
        for (var i = 1; i < samples.Length; i++)
        {
            pre[i] = samples[i] - 0.97 * samples[i - 1];
        }

        for (var start = 0; start + FrameLength <= samples.Length; start += FrameShift)
        {
            // 分帧 + 汉明窗。
            var real = new double[FftSize];
            var imag = new double[FftSize];
            for (var i = 0; i < FrameLength; i++)
            {
                var window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FrameLength - 1));
                real[i] = pre[start + i] * window;
            }

            SpectrumAnalyzer.Fft(real, imag);

            // 功率谱。
            var power = new double[FftSize / 2];
            for (var k = 0; k < FftSize / 2; k++)
            {
                power[k] = (real[k] * real[k] + imag[k] * imag[k]) / FftSize;
            }

            // Mel 滤波器组 → 对数能量。
            var logMel = new double[NumMelFilters];
            for (var m = 0; m < NumMelFilters; m++)
            {
                var energy = 0.0;
                for (var k = 0; k < FftSize / 2; k++)
                {
                    energy += power[k] * melFilters[m, k];
                }

                logMel[m] = Math.Log(Math.Max(energy, 1e-10));
            }

            // DCT-II → MFCC（前 13 维）。
            var mfcc = new double[NumCoeffs];
            for (var c = 0; c < NumCoeffs; c++)
            {
                var sum = 0.0;
                for (var m = 0; m < NumMelFilters; m++)
                {
                    sum += logMel[m] * Math.Cos(Math.PI * c * (m + 0.5) / NumMelFilters);
                }

                mfcc[c] = sum;
            }

            frames.Add(mfcc);
        }

        return frames.ToArray();
    }

    /// <summary>DTW 规整距离：两个 MFCC 序列的相似度，越小越相似。</summary>
    public static double DtwDistance(double[][] a, double[][] b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return double.PositiveInfinity;
        }

        var n = a.Length;
        var m = b.Length;
        var dtw = new double[n + 1, m + 1];
        for (var i = 0; i <= n; i++)
        {
            for (var j = 0; j <= m; j++)
            {
                dtw[i, j] = double.PositiveInfinity;
            }
        }

        dtw[0, 0] = 0;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = Euclidean(a[i - 1], b[j - 1]);
                dtw[i, j] = cost + Math.Min(
                    Math.Min(dtw[i - 1, j], dtw[i, j - 1]),
                    dtw[i - 1, j - 1]);
            }
        }

        // 按路径长度归一化，使不同长度模板可比。
        return dtw[n, m] / (n + m);
    }

    private static double Euclidean(double[] a, double[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }

        return Math.Sqrt(sum);
    }

    private static double[,] BuildMelFilters()
    {
        var maxHz = 8000;  // 16kHz 采样，Nyquist 8kHz。
        var maxMel = HzToMel(maxHz);

        // 26+2 个 Mel 均匀点。
        var melPoints = new double[NumMelFilters + 2];
        for (var i = 0; i < melPoints.Length; i++)
        {
            melPoints[i] = maxMel * i / (NumMelFilters + 1);
        }

        // 转回 Hz 对应的 FFT bin。
        var binPoints = new int[NumMelFilters + 2];
        for (var i = 0; i < melPoints.Length; i++)
        {
            binPoints[i] = (int)Math.Floor((FftSize + 1) * MelToHz(melPoints[i]) / 16000.0);
        }

        var filters = new double[NumMelFilters, FftSize / 2];
        for (var m = 1; m <= NumMelFilters; m++)
        {
            var left = binPoints[m - 1];
            var center = binPoints[m];
            var right = binPoints[m + 1];

            for (var k = left; k < center && k < FftSize / 2; k++)
            {
                filters[m - 1, k] = (k - left) / (double)(center - left);
            }

            for (var k = center; k < right && k < FftSize / 2; k++)
            {
                filters[m - 1, k] = (right - k) / (double)(right - center);
            }
        }

        return filters;
    }

    private static double HzToMel(double hz)
    {
        return 2595.0 * Math.Log10(1.0 + hz / 700.0);
    }

    private static double MelToHz(double mel)
    {
        return 700.0 * (Math.Pow(10.0, mel / 2595.0) - 1.0);
    }
}
