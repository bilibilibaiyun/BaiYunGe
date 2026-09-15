namespace BaiYunGe.Core.Audio;

/// <summary>
/// 能量 VAD，参考旧版健壮实现，并针对降噪麦克风调优：
/// 1) 前 250ms 用「低分位（25%）」校准噪声地板，并排除明显语音帧（> -40dB），
///    避免「按住立即说话」时校准被语音污染、噪声地板被抬高；
///    语音阈值 = max(语音下限, min(-36, 噪声地板+8))，动态自适应；
/// 2) 语音判定下限大幅放宽（-54dB），适配降噪麦克风整体电平偏低（实测 -50~-60dB）的场景；
/// 3) 只有「检测到语音之后」才开始累计静音时长（避免按住键未开口被提前停止）；
/// 4) 提交前的有效语音判定用宽松下限 -68dB。
/// </summary>
public sealed class VoiceActivityDetector
{
    private const double CalibrationDurationMs = 250;
    private const double MinimumSpeechDb = -68;

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

    /// <summary>送入一帧（RMS dB、已录时长秒、帧时长秒）。</summary>
    public void AddFrame(double rmsDb, double elapsedSeconds, double frameSeconds)
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
                _noiseFloorDb = Percentile25(_calibrationSamples);
            }
        }

        var threshold = ComputeThreshold();
        if (rmsDb >= threshold)
        {
            _hasSpeech = true;
            _silenceAccumulatedMs = 0;
            _silenceTimeoutReported = false;
            if (!_speechReported)
            {
                _speechReported = true;
                SpeechDetected?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

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

        return Math.Max(_speechFloorDb, Math.Min(-36, _noiseFloorDb + 8));
    }

    private static double Percentile25(List<double> samples)
    {
        var sorted = samples.OrderBy(x => x).ToArray();
        var index = Math.Clamp((int)(sorted.Length * 0.25), 0, sorted.Length - 1);
        return sorted[index];
    }
}
