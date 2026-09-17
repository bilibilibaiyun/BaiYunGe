using BaiYunGe.Core.Audio;
using BaiYunGe.Core.Inference;
using BaiYunGe.Core.Output;

namespace BaiYunGe.Core;

public enum PipelineStage
{
    Idle,
    Listening,
    Transcribing,
    Completed,
    Error
}

public sealed record PipelineResult(
    string Text,
    bool HasSpeech,
    AudioCaptureStopReason StopReason,
    OutputResult Output,
    Exception? Error)
{
    public bool Succeeded => Error is null && HasSpeech && !string.IsNullOrWhiteSpace(Text);

    public static PipelineResult Empty =>
        new(string.Empty, false, AudioCaptureStopReason.Requested, OutputResult.Failed, null);
}

/// <summary>
/// 串联一次识别会话：录音 → 静音门控 → llama-server 转写 → 词典替换 → 输出。
/// 一次会话只接受一个结束信号（松开/点按第二次/静音/Esc/超时），先到者生效。
/// </summary>
public sealed class RecognitionPipeline : IDisposable
{
    private readonly AppLogger _logger;
    private readonly AppPaths _paths;
    private readonly AudioCaptureService _audioCapture;
    private readonly LlamaServerManager _server;
    private readonly DictionaryProcessor _dictionary;
    private readonly AsrResponseParser _parser;
    private readonly TextOutputService _output;
    private readonly SynchronizationContext? _uiContext;
    private readonly object _sync = new();

    private AppSettings _settings = new();
    private Task<AudioCaptureResult>? _captureTask;
    private CancellationTokenSource? _captureCancellation;
    private string? _wavPath;
    private IntPtr _targetWindow;
    private int _state;
    private PipelineResult? _lastResult;
    private bool _disposed;

    public RecognitionPipeline(
        AppLogger logger,
        AppPaths paths,
        AudioCaptureService audioCapture,
        LlamaServerManager server,
        DictionaryProcessor dictionary,
        AsrResponseParser parser,
        TextOutputService output,
        AppSettings settings)
    {
        _logger = logger;
        _paths = paths;
        _audioCapture = audioCapture;
        _server = server;
        _dictionary = dictionary;
        _parser = parser;
        _output = output;
        _settings = settings;
        _uiContext = SynchronizationContext.Current;

        _audioCapture.LevelChanged += (_, level) => LevelChanged?.Invoke(this, level);
        _audioCapture.ErrorOccurred += (_, error) => ErrorOccurred?.Invoke(this, error);
    }

    public event EventHandler<PipelineStage>? StageChanged;

    public event EventHandler<float>? LevelChanged;

    public event EventHandler<string>? ErrorOccurred;

    public event EventHandler<PipelineResult>? Completed;

    public PipelineStage CurrentStage => (PipelineStage)Volatile.Read(ref _state);

    public void UpdateSettings(AppSettings settings)
    {
        lock (_sync)
        {
            _settings = settings;
        }
    }

    public async Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref _state, (int)PipelineStage.Listening, (int)PipelineStage.Idle) != (int)PipelineStage.Idle)
        {
            throw new InvalidOperationException($"Cannot start listening from state {CurrentStage}.");
        }

        AppSettings settings;
        lock (_sync)
        {
            settings = _settings;
        }

        _targetWindow = User32.GetForegroundWindow();
        _wavPath = _paths.CreateTempWavPath();
        _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        SetStage(PipelineStage.Listening);
        _logger.Info($"Recognition started. target=0x{_targetWindow.ToInt64():X} wav={_wavPath}");

        try
        {
            // hold 模式：按住即录音，静音不自动停止（仅松开键停止）；toggle 模式才启用静音自动停止。
            var silenceStopMs = settings.KeyboardMode == "toggle" ? settings.SilenceStopMs : 0;

            _captureTask = _audioCapture.StartAsync(
                settings.MicDeviceId,
                _wavPath,
                settings.MaxRecordSeconds,
                silenceStopMs,
                settings.VadSensitivity,
                settings.NoiseEnvironment,
                settings.InputGainDb,
                settings.CalibratedThresholdDb,
                _captureCancellation.Token);

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            if (_captureTask.IsFaulted)
            {
                await _captureTask.ConfigureAwait(false);
            }

            _ = ObserveCaptureCompletionAsync();
        }
        catch (Exception exception)
        {
            CleanupWav();
            var result = new PipelineResult(string.Empty, false, AudioCaptureStopReason.Error, OutputResult.Failed, exception);
            _lastResult = result;
            _logger.Error("Failed to start audio capture.", exception);
            Completed?.Invoke(this, result);
            SetStage(PipelineStage.Error);
            SetStage(PipelineStage.Idle);
            _wavPath = null;
            _captureCancellation?.Dispose();
            _captureCancellation = null;
        }
    }

    public async Task<PipelineResult> StopListeningAsync(bool cancel = false)
    {
        if (CurrentStage is PipelineStage.Idle or PipelineStage.Completed or PipelineStage.Error)
        {
            return PipelineResult.Empty;
        }

        if (cancel)
        {
            _audioCapture.Cancel();
            _captureCancellation?.Cancel();
        }
        else if (CurrentStage == PipelineStage.Listening)
        {
            await _audioCapture.StopAsync().ConfigureAwait(false);
        }

        return await CompleteCaptureAsync(cancel).ConfigureAwait(false);
    }

    public void Cancel()
    {
        if (CurrentStage != PipelineStage.Listening)
        {
            return;
        }

        _audioCapture.Cancel();
        _captureCancellation?.Cancel();
    }

    private async Task ObserveCaptureCompletionAsync()
    {
        try
        {
            if (_captureTask is null)
            {
                return;
            }

            await _captureTask.ConfigureAwait(false);
            var result = _captureTask.Result;
            var cancelled = _captureCancellation?.IsCancellationRequested == true ||
                            result.StopReason == AudioCaptureStopReason.Cancelled;
            await CompleteCaptureAsync(cancelled).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await CompleteCaptureAsync(true).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Error("Capture completion observer failed.", exception);
            await CompleteCaptureAsync(true).ConfigureAwait(false);
        }
    }

    private async Task<PipelineResult> CompleteCaptureAsync(bool cancel)
    {
        if (Interlocked.CompareExchange(ref _state, (int)PipelineStage.Transcribing, (int)PipelineStage.Listening) !=
            (int)PipelineStage.Listening)
        {
            return _lastResult ?? PipelineResult.Empty;
        }

        AudioCaptureResult captureResult;
        try
        {
            captureResult = _captureTask is null
                ? AudioCaptureResult.Empty
                : await _captureTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return await CompleteErrorAsync(exception).ConfigureAwait(false);
        }

        if (cancel)
        {
            CleanupWav();
            var cancelled = new PipelineResult(string.Empty, false, AudioCaptureStopReason.Cancelled, OutputResult.Failed, null);
            _lastResult = cancelled;
            _wavPath = null;
            _captureCancellation?.Dispose();
            _captureCancellation = null;
            Completed?.Invoke(this, cancelled);
            SetStage(PipelineStage.Idle);
            return cancelled;
        }

        // 丢弃两种情况：录音过短（误触）或 VAD 判定无语音（静音/纯杂音）。
        // VAD 用「噪声地板低分位 + 8dB」的相对阈值判断，能区分稳定杂音与语音，
        // 从源头阻止静音/杂音被提交转写。
        if (captureResult.Duration < TimeSpan.FromMilliseconds(300) || !captureResult.HasSpeech)
        {
            _logger.Info(
                $"No speech: peak={captureResult.PeakRmsDb:F1}dB hasSpeech={captureResult.HasSpeech} " +
                $"duration={captureResult.Duration.TotalSeconds:F1}s");
            CleanupWav();
            var noSpeech = new PipelineResult(string.Empty, false, captureResult.StopReason, OutputResult.Failed, null);
            _lastResult = noSpeech;
            _wavPath = null;
            _captureCancellation?.Dispose();
            _captureCancellation = null;
            Completed?.Invoke(this, noSpeech);
            SetStage(PipelineStage.Idle);
            return noSpeech;
        }

        try
        {
            AppSettings settings;
            lock (_sync)
            {
                settings = _settings;
            }

            SetStage(PipelineStage.Transcribing);
            await _server.EnsureStartedAsync(
                settings.ModelDirectory,
                settings.InferenceDevice,
                _captureCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);

            // 词典热词偏置（prompt 注入）已彻底移除：它是词典幻觉的根源（ASR 对小声/静音
            // 音频会把「参考词汇」抄进结果）。词典的别名替换仍由后处理 ReplaceAliases 完成，
            // 不受影响。识别时不注入任何词典 prompt。
            var speechMargin = captureResult.PeakRmsDb - captureResult.NoiseFloorDb;
            var prompt = string.Empty;
            // 识别语言独立于界面语言，默认 auto 由模型自动检测中英文。
            var language = string.IsNullOrWhiteSpace(settings.RecognitionLanguage)
                ? "auto"
                : settings.RecognitionLanguage;
            var rawText = await _server.TranscribeAsync(
                captureResult.WavPath,
                language,
                prompt,
                _captureCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);

            var parsed = _parser.Parse(rawText);
            // 词典列表可能在 UI 线程被增删：枚举前先快照，避免跨线程 Collection modified。
            var dictionarySnapshot = settings.Dictionary.ToList();
            var text = _dictionary.ReplaceAliases(parsed.Text, dictionarySnapshot, settings.DictionaryEnabled);

            // 有效性检测：转写结果为空/纯标点，或「词典 echo + 接近噪声地板」时视为幻觉，不键入。
            // 用相对噪声地板的余量判断「太小声/静音」，不受输入增益影响（增益会同时抬升峰值与噪声地板）。
            var isDictionaryEcho = IsDictionaryEcho(text, dictionarySnapshot, settings.DictionaryEnabled);
            var isTooQuiet = speechMargin < 6;
            if (IsEmptyOrHallucination(text, settings.Dictionary, settings.DictionaryEnabled) ||
                (isDictionaryEcho && isTooQuiet))
            {
                CleanupWav();
                _logger.Info($"No valid speech (empty or hallucination): margin={speechMargin:F1}dB text='{text}'");
                var noSpeech = new PipelineResult(string.Empty, false, captureResult.StopReason, OutputResult.Failed, null);
                _lastResult = noSpeech;
                _wavPath = null;
                _captureCancellation?.Dispose();
                _captureCancellation = null;
                Completed?.Invoke(this, noSpeech);
                SetStage(PipelineStage.Idle);
                return noSpeech;
            }

            CleanupWav();

            // 转写结束：立即释放会话资源并回到 Idle（不再停留在 Completed 过渡态），
            // 使「上屏 / 结果展示」期间再次按快捷键也能正常唤醒。上屏用局部 target 变量，
            // 不受后续新识别覆盖字段的影响。
            var target = _targetWindow;
            _wavPath = null;
            _captureCancellation?.Dispose();
            _captureCancellation = null;
            SetStage(PipelineStage.Idle);

            var outputResult = await OutputOnUiAsync(text, target, settings.OutputMethod, CancellationToken.None)
                .ConfigureAwait(false);

            var result = new PipelineResult(text, true, captureResult.StopReason, outputResult, null);
            _lastResult = result;
            Completed?.Invoke(this, result);
            return result;
        }
        catch (Exception exception)
        {
            return await CompleteErrorAsync(exception).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 判断转写文本是否无有效内容：空 / 纯标点。VAD（v2.1.1 恢复）已从源头拦截静音/杂音，
    /// 因此不再做「移除词典词后为空即判 echo」的检测——那会把用户说出的词典词整句误杀。
    /// </summary>
    private static bool IsEmptyOrHallucination(
        string text,
        IReadOnlyList<DictionaryEntry> dictionary,
        bool dictionaryEnabled)
    {
        // 保留参数与签名不变：词典 echo 检测已移除，静音/杂音由 VAD 负责拦截。
        _ = dictionary;
        _ = dictionaryEnabled;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        // 剥离标点、符号与空白后的实际内容。
        var meaningful = string.Empty;
        foreach (var c in text)
        {
            if (!char.IsPunctuation(c) && !char.IsSymbol(c) && !char.IsWhiteSpace(c))
            {
                meaningful += c;
            }
        }

        return string.IsNullOrWhiteSpace(meaningful);
    }

    /// <summary>
    /// 词典 echo 检测：识别结果是否「像」词典里的某个词（完整匹配，或编辑距离很近——
    /// 同音字/形近字幻觉）。用于后处理兜底：小声/低信噪比时 ASR 易把环境声幻觉成词典词汇。
    /// </summary>
    private static bool IsDictionaryEcho(string text, IReadOnlyList<DictionaryEntry> dictionary, bool dictionaryEnabled)
    {
        if (!dictionaryEnabled || dictionary is null || dictionary.Count == 0)
        {
            return false;
        }

        var normalized = NormalizeText(text);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        foreach (var entry in dictionary)
        {
            foreach (var word in new[] { entry.Target, entry.Alias })
            {
                var w = NormalizeText(word);
                if (string.IsNullOrEmpty(w))
                {
                    continue;
                }

                if (normalized.Equals(w, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 编辑距离阈值按词长放宽：短词 1，长词最多 2，避免把完全无关的短词误判。
                var maxDistance = Math.Max(1, w.Length / 3);
                if (Levenshtein(normalized, w) <= maxDistance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string NormalizeText(string text)
    {
        var result = string.Empty;
        foreach (var c in text)
        {
            if (!char.IsPunctuation(c) && !char.IsSymbol(c) && !char.IsWhiteSpace(c))
            {
                result += c;
            }
        }

        return result;
    }

    private static int Levenshtein(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            dp[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            dp[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Length, b.Length];
    }

    private async Task<PipelineResult> CompleteErrorAsync(Exception exception)
    {
        CleanupWav();
        _wavPath = null;

        var result = new PipelineResult(string.Empty, false, AudioCaptureStopReason.Error, OutputResult.Failed, exception);
        _lastResult = result;
        _logger.Error("Recognition failed.", exception);
        ErrorOccurred?.Invoke(this, exception.Message);
        Completed?.Invoke(this, result);
        _captureCancellation?.Dispose();
        _captureCancellation = null;
        SetStage(PipelineStage.Error);
        SetStage(PipelineStage.Idle);
        await Task.CompletedTask;
        return result;
    }

    private async Task<OutputResult> OutputOnUiAsync(string text, IntPtr target, string method, CancellationToken ct)
    {
        if (_uiContext is null)
        {
            return await _output.OutputAsync(text, target, method, ct).ConfigureAwait(false);
        }

        var tcs = new TaskCompletionSource<OutputResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(async _ =>
        {
            try
            {
                tcs.TrySetResult(await _output.OutputAsync(text, target, method, ct).ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                tcs.TrySetException(exception);
            }
        }, null);

        return await tcs.Task.ConfigureAwait(false);
    }

    private void CleanupWav()
    {
        var path = _wavPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to delete temporary WAV {path}: {exception.Message}");
        }
    }

    private void SetStage(PipelineStage stage)
    {
        Volatile.Write(ref _state, (int)stage);
        StageChanged?.Invoke(this, stage);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cancel();
        _captureCancellation?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RecognitionPipeline));
        }
    }
}
