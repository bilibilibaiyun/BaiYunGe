namespace BaiYunGe.Core.Audio;

/// <summary>
/// 能量 VAD，针对降噪麦克风调优：
/// 1) 前 250ms 用「中位数」校准噪声地板，并排除明显语音帧（> -40dB），
///    避免「按住立即说话」时校准被语音污染、噪声地板被抬高；
///    语音阈值 = max(语音下限, min(-36, 噪声地板+12))，动态自适应；
/// 2) 需连续 3 帧超过阈值才判语音，过滤瞬时环境杂音尖峰（键盘声、物体碰撞等）；
/// 3) 语音判定下限放宽（-54dB），适配降噪麦克风整体电平偏低（实测 -50~-60dB）的场景；
/// 4) 只有「检测到语音之后」才开始累计静音时长（避免按住键未开口被提前停止）；
/// 5) 提交前的有效语音判定用宽松下限 -68dB。
/// </summary>
public sealed class VoiceActivityDetector
{
    private const double CalibrationDurationMs = 250;
    private const double MinimumSpeechDb = -68;

    /// <summary>连续超过阈值的帧数达到该值才判为语音，过滤瞬时环境杂音尖峰（键盘声、物体碰撞等）。</summary>
    private const int ConsecutiveSpeechFramesRequired = 3;

    // 频谱分析参数：16kHz 采样，512 点 FFT，分辨率 31.25Hz。
    // 人声能量集中在语音频段，风扇等持续低频杂音能量集中在 <187Hz。
    // 语音频段下限取 ~187Hz（bin 6）：中文低音调（如「你好」）的基频（100~200Hz）
    // 与低次谐波在 300Hz 以下，若下限取 300Hz 会被排除导致语音占比偏低误判无语音。
    private const int FftSize = 512;
    private const int SampleRate = 16000;
    private const int VoiceBandStartBin = 6;   // 约 187Hz
    private const int VoiceBandEndBin = 109;   // 约 3406Hz
    /// <summary>语音频段能量占全频段（除 DC）的比例阈值，高于此值视为人声。</summary>
    private const double VoiceRatioThreshold = 0.40;

    private readonly double _silenceStopMs;
    private readonly double _speechFloorDb;
    private readonly List<double> _calibrationSamples = new();

    private double _calibrationElapsedMs;
    private double _noiseFloorDb;
    private double _silenceAccumulatedMs;
    private double _peakRmsDb = double.NegativeInfinity;
    private bool _hasSpeech;
    private bool _speechReported;
    private bool _silenceTimeoutReported;
    private int _consecutiveSpeechFrames;
    private readonly List<float> _spectrumBuffer = new();
    private double _lastVoiceRatio;

    public VoiceActivityDetector(int sensitivity, int silenceStopMs)
    {
        // silenceStopMs <= 0 表示禁用静音自动停止（hold 模式：按住即录音，仅松开键停止）。
        _silenceStopMs = silenceStopMs <= 0
            ? double.PositiveInfinity
            : Math.Max(200, silenceStopMs);
        // 语音判定阈值下限：降噪麦克风电平偏低，阈值相应下调。
        _speechFloorDb = sensitivity switch
        {
            <= 0 => -50,
            1 => -54,
            _ => -58
        };
    }

    public event EventHandler? SpeechDetected;

    public event EventHandler? SilenceTimeout;

    public double PeakRmsDb => _peakRmsDb;

    public bool HasSpeech => _hasSpeech;

    /// <summary>最近一次频谱分析的语音频段能量占比（0~1，调试诊断用）。</summary>
    public double LastVoiceRatio => _lastVoiceRatio;

    /// <summary>送入一帧（PCM 采样、RMS dB、已录时长秒、帧时长秒）。</summary>
    public void AddFrame(float[] samples, double rmsDb, double elapsedSeconds, double frameSeconds)
    {
        if (!double.IsFinite(rmsDb))
        {
            return;
        }

        _peakRmsDb = Math.Max(_peakRmsDb, rmsDb);

        // 前 250ms 采集噪声地板样本。明显高于底噪（> -40dB）的帧视为语音，不计入校准，
        // 避免「按住立即说话」时校准样本被语音污染、噪声地板被抬高。
        if (_calibrationElapsedMs < CalibrationDurationMs)
        {
            if (rmsDb <= -40)
            {
                _calibrationSamples.Add(rmsDb);
            }

            _calibrationElapsedMs += frameSeconds * 1000.0;
            if (_calibrationElapsedMs >= CalibrationDurationMs && _calibrationSamples.Count > 0)
            {
                _noiseFloorDb = Median(_calibrationSamples);
            }
        }

        // 累积采样到 512（约 32ms）再做 FFT：10ms 短帧对低频（基频 100~200Hz）频谱泄漏严重，
        // 累积到 32ms 后基频有 4~8 个周期，语音频段占比计算更准（修复「你好」等低音调词检测不到）。
        _spectrumBuffer.AddRange(samples);
        while (_spectrumBuffer.Count >= FftSize)
        {
            var frame = new float[FftSize];
            _spectrumBuffer.CopyTo(0, frame, 0, FftSize);
            _spectrumBuffer.RemoveRange(0, FftSize);
            _lastVoiceRatio = ComputeVoiceRatio(frame);
        }

        var threshold = ComputeThreshold();
        // 人声判定：能量超过阈值，且语音频段能量占优——用频谱区分持续低频杂音（风扇等）与真实人声。
        var voiceRatio = _lastVoiceRatio;
        var isVoice = rmsDb >= threshold && voiceRatio >= VoiceRatioThreshold;
        if (isVoice)
        {
            // 需要连续多帧判为语音才置位：瞬时杂音尖峰（单帧）不触发，持续的人声才会。
            _consecutiveSpeechFrames++;
            if (_consecutiveSpeechFrames >= ConsecutiveSpeechFramesRequired)
            {
                _hasSpeech = true;
                _silenceAccumulatedMs = 0;
                _silenceTimeoutReported = false;
                if (!_speechReported)
                {
                    _speechReported = true;
                    SpeechDetected?.Invoke(this, EventArgs.Empty);
                }
            }

            return;
        }

        _consecutiveSpeechFrames = 0;

        // 关键：只有检测到语音之后才累计静音，避免按住键未开口就被静音超时停止。
        if (_hasSpeech)
        {
            _silenceAccumulatedMs += frameSeconds * 1000.0;
            if (!_silenceTimeoutReported && _silenceAccumulatedMs >= _silenceStopMs)
            {
                _silenceTimeoutReported = true;
                SilenceTimeout?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>整段录音是否包含有效语音（宽松下限，避免误判）。</summary>
    public bool HasUsableSpeech()
    {
        return _hasSpeech && _peakRmsDb >= MinimumSpeechDb;
    }

    private double ComputeThreshold()
    {
        if (_calibrationElapsedMs < CalibrationDurationMs)
        {
            return _speechFloorDb;
        }

        return Math.Max(_speechFloorDb, Math.Min(-36, _noiseFloorDb + 12));
    }

    private static double Median(List<double> samples)
    {
        var sorted = samples.OrderBy(x => x).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }

    /// <summary>
    /// 计算语音频段（300~3400Hz）能量占全频段（除 DC 直流分量）的比例。
    /// 人声能量集中在语音频段（占比高），风扇等持续低频杂音能量集中在 &lt;300Hz（占比低），
    /// 白噪声各频段均匀（占比约 38%）。返回 0~1。
    /// </summary>
    private static double ComputeVoiceRatio(float[] samples)
    {
        if (samples is null || samples.Length == 0)
        {
            return 0;
        }

        // 补零到 FftSize，做实数 FFT（虚部置 0）。加汉宁窗减少频谱泄漏（对短时低频尤其重要）。
        var real = new double[FftSize];
        var imag = new double[FftSize];
        var count = Math.Min(samples.Length, FftSize);
        for (var i = 0; i < count; i++)
        {
            var window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (count - 1)));
            real[i] = samples[i] * window;
        }

        Fft(real, imag);

        // 累加幅度谱能量（跳过 DC bin 0）。
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
    private static void Fft(double[] real, double[] imag)
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
