using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BaiYunGe.Core.Audio;

public enum AudioCaptureStopReason
{
    Requested,
    Silence,
    MaximumDuration,
    Cancelled,
    DeviceStopped,
    Error
}

public sealed record AudioCaptureResult(
    string WavPath,
    TimeSpan Duration,
    bool HasSpeech,
    double PeakRmsDb,
    AudioCaptureStopReason StopReason)
{
    public static AudioCaptureResult Empty =>
        new(string.Empty, TimeSpan.Zero, false, double.NegativeInfinity, AudioCaptureStopReason.Requested);
}

/// <summary>
/// WASAPI 共享模式录音，任意设备格式统一转 16kHz/16bit/单声道 PCM 写 WAV。
///
/// 关键约束（历史竞态教训）：WAV 文件头只有在 writer 被 Flush/Dispose 时才回填
/// data/RIFF 大小；因此录音 Task 必须等 writer 释放之后才完成，否则识别侧会读到
/// 文件头仍为占位 0 的坏 WAV（表现为「第一次识别成功、之后持续 400 报错」）。
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private readonly AudioDeviceEnumerator _deviceEnumerator = new();
    private readonly PcmAudioConverter _converter = new();
    private readonly object _sync = new();

    private CaptureSession? _session;
    private bool _disposed;

    public event EventHandler<float>? LevelChanged;

    public event EventHandler<string>? ErrorOccurred;

    public event EventHandler<AudioDeviceInfo>? DeviceChanged;

    public IReadOnlyList<AudioDeviceInfo> GetDevices() => _deviceEnumerator.GetCaptureDevices();

    public Task<AudioCaptureResult> StartAsync(
        string? deviceId,
        string outputPath,
        int maxRecordSeconds,
        int silenceStopMs,
        int vadSensitivity,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        CaptureSession session;
        lock (_sync)
        {
            if (_session is not null)
            {
                throw new InvalidOperationException("Audio capture is already running.");
            }

            var device = _deviceEnumerator.Resolve(deviceId);
            var vad = new VoiceActivityDetector(vadSensitivity, silenceStopMs);
            session = new CaptureSession(
                device,
                outputPath,
                vad,
                _converter,
                level => LevelChanged?.Invoke(this, level),
                maxRecordSeconds,
                StopSessionAsync);
            _session = session;
        }

        return RunSessionAsync(session, cancellationToken);
    }

    public Task<AudioCaptureResult> StopAsync()
    {
        CaptureSession? session;
        lock (_sync)
        {
            session = _session;
        }

        return session is null
            ? Task.FromResult(AudioCaptureResult.Empty)
            : StopSessionAsync(session, AudioCaptureStopReason.Requested);
    }

    public void Cancel()
    {
        CaptureSession? session;
        lock (_sync)
        {
            session = _session;
        }

        if (session is not null)
        {
            _ = StopSessionAsync(session, AudioCaptureStopReason.Cancelled);
        }
    }

    private async Task<AudioCaptureResult> RunSessionAsync(CaptureSession session, CancellationToken cancellationToken)
    {
        try
        {
            // WASAPI StartRecording 在设备异常（睡眠唤醒、屏幕共享改变音频路由等）时可能阻塞。
            // 放到后台线程并加超时，避免阻塞 UI 线程导致整个软件卡死。
            try
            {
                await Task.Run(session.Start, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                session.Dispose();
                throw new InvalidOperationException("音频设备启动超时，请重新插拔麦克风后重试。");
            }

            DeviceChanged?.Invoke(this, session.DeviceInfo);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (session.MaximumDuration < TimeSpan.MaxValue)
            {
                linked.CancelAfter(session.MaximumDuration);
            }

            using var registration = linked.Token.Register(
                () => _ = StopSessionAsync(session, AudioCaptureStopReason.MaximumDuration));

            return await session.Completion.Task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            session.Dispose();
            ErrorOccurred?.Invoke(this, exception.Message);
            throw;
        }
    }

    private async Task<AudioCaptureResult> StopSessionAsync(CaptureSession session, AudioCaptureStopReason reason)
    {
        // 让出当前线程，避免在 WASAPI 回调线程里做重活。
        await Task.Yield();
        var result = session.Stop(reason);

        lock (_sync)
        {
            if (ReferenceEquals(_session, session))
            {
                _session = null;
            }
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            _session?.Dispose();
            _session = null;
        }

        _deviceEnumerator.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioCaptureService));
        }
    }

    private sealed class CaptureSession : IDisposable
    {
        private readonly WasapiCapture _capture;
        private readonly WaveFileWriter _writer;
        private readonly VoiceActivityDetector _vad;
        private readonly PcmAudioConverter _converter;
        private readonly Action<float> _levelCallback;
        private readonly Stopwatch _clock = new();
        private readonly TaskCompletionSource<AudioCaptureResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _stopState;

        public CaptureSession(
            NAudio.CoreAudioApi.MMDevice device,
            string outputPath,
            VoiceActivityDetector vad,
            PcmAudioConverter converter,
            Action<float> levelCallback,
            int maxRecordSeconds,
            Func<CaptureSession, AudioCaptureStopReason, Task<AudioCaptureResult>> stopCallback)
        {
            // 显式指定 16kHz/16 位/mono：NAudio 的 WasapiCapture 带 AutoConvertPcm 标志，
            // WASAPI 会把设备原生格式（含专业声卡如 Focusrite 的 8 位/24 位 MixFormat）
            // 自动转换为目标格式，避免用 MixFormat 录音导致 8 位低质量、识别失败。
            _capture = new WasapiCapture(device)
            {
                WaveFormat = new WaveFormat(PcmAudioConverter.TargetSampleRate, 16, 1)
            };
            _vad = vad;
            _converter = converter;
            _levelCallback = levelCallback;
            // maxRecordSeconds <= 0 表示不限时长（仅靠静音超时/松开按键结束）。
            MaximumDuration = maxRecordSeconds > 0
                ? TimeSpan.FromSeconds(maxRecordSeconds)
                : TimeSpan.MaxValue;
            StopCallback = stopCallback;

            var info = new AudioDeviceInfo(
                device.ID,
                device.FriendlyName,
                device.AudioClient.MixFormat?.Channels ?? 1,
                device.AudioClient.MixFormat?.SampleRate ?? 48000,
                false);
            DeviceInfo = info;

            _writer = new WaveFileWriter(outputPath, new WaveFormat(PcmAudioConverter.TargetSampleRate, 1));

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _vad.SilenceTimeout += OnSilenceTimeout;
        }

        public TaskCompletionSource<AudioCaptureResult> Completion => _completion;

        public TimeSpan MaximumDuration { get; }

        public AudioDeviceInfo DeviceInfo { get; }

        public Func<CaptureSession, AudioCaptureStopReason, Task<AudioCaptureResult>> StopCallback { get; }

        public void Start()
        {
            _clock.Start();
            _capture.StartRecording();
        }

        /// <summary>
        /// 停录音并交付结果。顺序至关重要：先停录音，再释放 writer（回填 WAV 头），
        /// 最后才完成 completion，保证任何读取方拿到的 WAV 文件头都完整。
        /// </summary>
        public AudioCaptureResult Stop(AudioCaptureStopReason reason)
        {
            if (Interlocked.Exchange(ref _stopState, 1) != 0)
            {
                // 已停止：等待既有结果。
                return Completion.Task.IsCompletedSuccessfully ? Completion.Task.Result : AudioCaptureResult.Empty;
            }

            _clock.Stop();

            try
            {
                if (_capture.CaptureState is CaptureState.Capturing or CaptureState.Starting or CaptureState.Stopping)
                {
                    _capture.StopRecording();
                }
            }
            catch
            {
                // 结果仍需交付。
            }

            var result = CreateResult(reason);

            try
            {
                Dispose();
            }
            catch
            {
                // 释放失败不阻塞结果交付。
            }

            _completion.TrySetResult(result);
            return result;
        }

        public void Dispose()
        {
            try
            {
                if (_capture.CaptureState is CaptureState.Capturing or CaptureState.Starting or CaptureState.Stopping)
                {
                    _capture.StopRecording();
                }
            }
            catch
            {
            }

            _capture.Dispose();
            _writer.Dispose();
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs args)
        {
            try
            {
                var mono = _converter.ToMonoFloat(args.Buffer, args.BytesRecorded, _capture.WaveFormat);
                var resampled = _converter.Resample(mono, _capture.WaveFormat.SampleRate);
                var pcm16 = _converter.ToPcm16(resampled);
                _writer.Write(pcm16, 0, pcm16.Length);

                var rmsDb = CalculateRmsDb(resampled);
                var frameSeconds = resampled.Length / (double)PcmAudioConverter.TargetSampleRate;
                _vad.AddFrame(resampled, rmsDb, _clock.Elapsed.TotalSeconds, frameSeconds);
                _levelCallback((float)rmsDb);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
                _ = StopCallback(this, AudioCaptureStopReason.Error);
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs args)
        {
            // 设备/流异常停止，且尚未进入正常停止流程。
            if (Volatile.Read(ref _stopState) == 0)
            {
                _completion.TrySetException(new AudioDeviceStoppedException());
                _ = StopCallback(this, AudioCaptureStopReason.DeviceStopped);
            }
        }

        private void OnSilenceTimeout(object? sender, EventArgs args)
        {
            if (Volatile.Read(ref _stopState) == 0)
            {
                _ = StopCallback(this, AudioCaptureStopReason.Silence);
            }
        }

        private AudioCaptureResult CreateResult(AudioCaptureStopReason reason)
        {
            return new AudioCaptureResult(
                _writer.Filename,
                _clock.Elapsed,
                _vad.HasUsableSpeech(),
                _vad.PeakRmsDb,
                reason);
        }

        private static double CalculateRmsDb(IReadOnlyList<float> samples)
        {
            if (samples.Count == 0)
            {
                return -100;
            }

            var sum = 0d;
            foreach (var sample in samples)
            {
                sum += sample * sample;
            }

            var rms = Math.Sqrt(sum / samples.Count);
            return 20d * Math.Log10(Math.Max(rms, 1e-9));
        }
    }

    private sealed class AudioDeviceStoppedException : Exception
    {
        public AudioDeviceStoppedException()
            : base("The selected audio capture device stopped unexpectedly.")
        {
        }
    }
}
