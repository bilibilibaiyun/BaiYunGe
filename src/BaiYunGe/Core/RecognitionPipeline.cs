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
                0, // 0 = 不限录音时长。
                silenceStopMs,
                settings.VadSensitivity,
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

        // 能量 VAD 对低电平/降噪麦克风不可靠（语音峰值可低至 -60dB），
        // 故不做能量门控，仅凭「录音时长」判断是否有内容，交给 llama-server 自行识别。
        // 录音过短（<0.3s）视为误触丢弃。
        if (captureResult.Duration < TimeSpan.FromMilliseconds(300))
        {
            _logger.Info(
                $"Too short: peak={captureResult.PeakRmsDb:F1}dB stopReason={captureResult.StopReason} " +
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

            var prompt = BuildPrompt(settings);
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
            var text = _dictionary.ReplaceAliases(parsed.Text, settings.Dictionary, settings.DictionaryEnabled);

            CleanupWav();
            SetStage(PipelineStage.Completed);

            var outputResult = await OutputOnUiAsync(text, _targetWindow, settings.OutputMethod, CancellationToken.None)
                .ConfigureAwait(false);

            var result = new PipelineResult(text, true, captureResult.StopReason, outputResult, null);
            _lastResult = result;
            _wavPath = null;
            _captureCancellation?.Dispose();
            _captureCancellation = null;
            Completed?.Invoke(this, result);
            SetStage(PipelineStage.Idle);
            return result;
        }
        catch (Exception exception)
        {
            return await CompleteErrorAsync(exception).ConfigureAwait(false);
        }
    }

    private async Task<PipelineResult> CompleteErrorAsync(Exception exception)
    {
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

    private string BuildPrompt(AppSettings settings)
    {
        if (!settings.DictionaryEnabled)
        {
            return string.Empty;
        }

        var words = _dictionary.BuildPrompt(settings.Dictionary, true);
        return string.IsNullOrWhiteSpace(words) ? string.Empty : $"{settings.PromptPrefix}{words}";
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
