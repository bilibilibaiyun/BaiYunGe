using System.Threading;
using System.Windows;
using System.Windows.Media;
using BaiYunGe.Core;
using BaiYunGe.Core.Audio;
using BaiYunGe.Core.Inference;
using BaiYunGe.Core.Keyboard;
using BaiYunGe.Core.Output;
using BaiYunGe.UI;

namespace BaiYunGe;

public partial class App : Application
{
    private const string MutexName = "BaiYunGe_SingleInstance_2.0";

    private Mutex? _mutex;
    private AppLogger? _logger;
    private AppPaths? _paths;
    private AppSettingsStore? _store;
    private AppSettings? _settings;
    private LocalizedText? _text;
    private AudioCaptureService? _audioCapture;
    private LlamaServerManager? _server;
    private KeyboardHotkeyService? _hotkeyService;
    private RecognitionPipeline? _pipeline;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private OverlayWindow? _overlay;

    private bool _paused;
    private bool _listening;
    private DateTime _listeningStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例。
        _mutex = new Mutex(true, MutexName, out var isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        var args = ParseArgs(e.Args);
        _paths = new AppPaths(args.ContainsKey("--data-dir") ? args["--data-dir"] : null);
        _paths.EnsureDirectories();

        _logger = new AppLogger(_paths.Logs);
        _logger.Info($"BaiYunGe started. args={string.Join(' ', e.Args)}");

        _store = new AppSettingsStore(_logger, _paths.ConfigFile);
        _settings = _store.Load();
        ApplyTheme(_settings.Theme);

        _text = new LocalizedText();
        _text.SetLanguage(_settings.IsEnglish ? AppLanguage.EnUs : AppLanguage.ZhCn);

        _audioCapture = new AudioCaptureService();
        _server = new LlamaServerManager(_logger);
        _hotkeyService = new KeyboardHotkeyService();
        var dictionary = new DictionaryProcessor();
        var parser = new AsrResponseParser();
        var output = new TextOutputService(_logger);

        _pipeline = new RecognitionPipeline(
            _logger,
            _paths,
            _audioCapture,
            _server,
            dictionary,
            parser,
            output,
            _settings);

        _pipeline.StageChanged += OnStageChanged;
        _pipeline.LevelChanged += OnLevelChanged;
        _pipeline.Completed += OnPipelineCompleted;
        _pipeline.ErrorOccurred += OnPipelineError;

        _hotkeyService.SetHotkey(ParseHotkey(_settings.KeyboardShortcut));
        _hotkeyService.WakePressed += OnWakePressed;
        _hotkeyService.WakeReleased += OnWakeReleased;
        _hotkeyService.EscapePressed += OnEscapePressed;

        try
        {
            _hotkeyService.Start();
        }
        catch (Exception exception)
        {
            _logger.Error("Failed to install keyboard hook.", exception);
        }

        _tray = new TrayService(_text);
        _tray.OpenSettings += (_, _) => ShowSettings();
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.PauseChanged += (_, paused) =>
        {
            _paused = paused;
            _logger.Info($"Input {(paused ? "paused" : "resumed")}.");
        };

        _logger.Info("BaiYunGe ready.");

        // 模型就绪时后台预热 llama-server，消除首次识别的冷启动延迟（约 5 秒加载模型）。
        WarmupServerInBackground();

        // 首次运行自动弹设置（选择模型目录）。
        if (string.IsNullOrWhiteSpace(_settings.ModelDirectory) || args.ContainsKey("--settings"))
        {
            ShowSettings();
        }
    }

    /// <summary>
    /// 后台静默预热 llama-server：模型目录已配置且确有 gguf 文件时，
    /// 提前把模型加载进显存/内存，使首次识别与后续识别同样快。
    /// 全程不阻塞 UI，失败静默降级（首次识别时再走懒启动）。
    /// </summary>
    private void WarmupServerInBackground()
    {
        var modelDir = _settings!.ModelDirectory;
        if (string.IsNullOrWhiteSpace(modelDir) || !Directory.Exists(modelDir))
        {
            return;
        }

        // 全新设备尚未下载模型时跳过预热，避免无谓启动失败。
        try
        {
            if (Directory.GetFiles(modelDir, "*.gguf", SearchOption.TopDirectoryOnly).Length == 0)
            {
                return;
            }
        }
        catch
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _server!.EnsureStartedAsync(modelDir, _settings.InferenceDevice, CancellationToken.None);
                _logger!.Info("llama-server warmed up.");
            }
            catch (Exception exception)
            {
                _logger!.Warn($"Model warmup skipped: {exception.Message}");
            }
        });
    }

    private void OnWakePressed(object? sender, EventArgs e)
    {
        if (_paused)
        {
            return;
        }

        var stage = _pipeline!.CurrentStage;
        if (stage == PipelineStage.Listening)
        {
            // 点按模式：录音中第二次按下结束。
            if (_settings!.KeyboardMode == "toggle")
            {
                _ = StopListeningAsync();
            }

            return;
        }

        if (stage == PipelineStage.Transcribing)
        {
            // 识别进行中：忽略本次按键，避免状态冲突。
            return;
        }

        _ = StartListeningAsync();
    }

    private void OnWakeReleased(object? sender, EventArgs e)
    {
        if (_settings!.KeyboardMode == "hold" && _listening)
        {
            _ = StopListeningAsync();
        }
    }

    private void OnEscapePressed(object? sender, EventArgs e)
    {
        if (_listening)
        {
            _pipeline!.Cancel();
        }
    }

    private async Task StartListeningAsync()
    {
        _listening = true;
        _listeningStarted = DateTime.Now;

        if (_settings!.KeyboardMode == "toggle")
        {
            _hotkeyService!.SuppressWakeKey();
        }

        try
        {
            await _pipeline!.StartListeningAsync();
        }
        catch (Exception exception)
        {
            _logger!.Error("Failed to start listening.", exception);
            _listening = false;
        }
        finally
        {
            _hotkeyService!.ReleaseWakeKey();
        }
    }

    private async Task StopListeningAsync()
    {
        try
        {
            await _pipeline!.StopListeningAsync();
        }
        catch (Exception exception)
        {
            _logger!.Error("Failed to stop listening.", exception);
        }
        finally
        {
            _listening = false;
        }
    }

    private void OnStageChanged(object? sender, PipelineStage stage)
    {
        switch (stage)
        {
            case PipelineStage.Listening:
                // StageChanged 经 ThreadPool 在后台线程派发，WPF 控件必须切回 UI 线程。
                Dispatcher.Invoke(ShowOverlay);
                break;
            case PipelineStage.Transcribing:
                // 录音结束进入转写：弹窗切换到「转化中」。
                Dispatcher.Invoke(() => _overlay?.ShowMessage(_text!.Get("Overlay.Transcribing")));
                break;
            case PipelineStage.Idle:
                // 结束后由 Completed 事件决定是否隐藏。
                break;
        }
    }

    private void OnLevelChanged(object? sender, float level)
    {
        if (!_listening)
        {
            return;
        }

        var elapsed = DateTime.Now - _listeningStarted;
        var text = _text!.Get("Overlay.Listening");
        Dispatcher.Invoke(() => _overlay?.ShowListening(NormalizeLevel(level), $"{elapsed.TotalSeconds:F1}s"));
    }

    private void OnPipelineCompleted(object? sender, PipelineResult result)
    {
        Dispatcher.Invoke(() =>
        {
            if (_overlay is null)
            {
                return;
            }

            if (result.Error is not null)
            {
                _overlay.ShowMessage($"{_text!.Get("Overlay.Error")}: {result.Error.Message}");
            }
            else if (!result.HasSpeech)
            {
                _overlay.ShowMessage(_text!.Get("Overlay.NoSpeech"));
            }
            else if (result.Output == OutputResult.CopiedToClipboard)
            {
                _overlay.ShowMessage(_text!.Get("Overlay.Copied"));
            }
            else
            {
                _overlay.ShowMessage(_text!.Get("Overlay.Done"));
            }

            _ = HideOverlayAfterDelayAsync();
        });
    }

    private void OnPipelineError(object? sender, string message)
    {
        Dispatcher.Invoke(() => _overlay?.ShowMessage($"{_text!.Get("Overlay.Error")}: {message}"));
    }

    private async Task HideOverlayAfterDelayAsync()
    {
        await Task.Delay(1200);
        Dispatcher.Invoke(() => _overlay?.Hide());
    }

    private void ShowOverlay()
    {
        _overlay ??= new OverlayWindow();
        _overlay.ShowMessage(_text!.Get("Overlay.Listening"));
        if (!_overlay.IsVisible)
        {
            _overlay.Show();
        }
    }

    private void ShowSettings()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(
                _text!,
                _settings!,
                _store!,
                _audioCapture!,
                _hotkeyService!,
                new ModelDownloader(_logger),
                _logger!);
            _mainWindow.SettingsChanged += () =>
            {
                _hotkeyService!.SetHotkey(ParseHotkey(_settings!.KeyboardShortcut));
                _text!.SetLanguage(_settings.IsEnglish ? AppLanguage.EnUs : AppLanguage.ZhCn);
                ApplyTheme(_settings.Theme);
            };
            _mainWindow.ModelInstalled += async () =>
            {
                // 模型替换后重启推理服务。
                await _server!.StopAsync();
            };
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("BaiYunGe exiting.");
        _pipeline?.Dispose();
        _hotkeyService?.Dispose();
        _server?.Dispose();
        _audioCapture?.Dispose();
        _tray?.Dispose();
        _overlay?.Close();
        _logger?.Dispose();
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }

    private static HotkeyDefinition ParseHotkey(string? text)
    {
        return HotkeyDefinition.TryParse(text, out var hotkey) ? hotkey : HotkeyDefinition.Empty;
    }

    /// <summary>
    /// 应用主题（dark/light）：替换全局主题色资源，MainWindow 与 OverlayWindow
    /// 通过 DynamicResource 引用，切换后两个窗口立即生效。
    /// </summary>
    private static void ApplyTheme(string? theme)
    {
        var dark = !string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
        var res = Current.Resources;

        void Set(string key, string hex)
        {
            res[key] = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }

        if (dark)
        {
            Set("Theme.WindowBg", "#15151A");
            Set("Theme.SidebarBg", "#1C1C24");
            Set("Theme.CardBg", "#242430");
            Set("Theme.Accent", "#4D8BFF");
            Set("Theme.TextPrimary", "#EEEEF2");
            Set("Theme.TextSecondary", "#8A8A9E");
            Set("Theme.Border", "#33333F");
            Set("Theme.Hover", "#303040");
        }
        else
        {
            Set("Theme.WindowBg", "#F5F6FA");
            Set("Theme.SidebarBg", "#EBEDF3");
            Set("Theme.CardBg", "#FFFFFF");
            Set("Theme.Accent", "#3B7BEB");
            Set("Theme.TextPrimary", "#1A1C24");
            Set("Theme.TextSecondary", "#6A6E7C");
            Set("Theme.Border", "#D9DCE3");
            Set("Theme.Hover", "#E3E6ED");
        }
    }

    private static float NormalizeLevel(float rmsDb)
    {
        // -60dB .. 0dB 映射到 0..1。
        return (float)Math.Clamp((rmsDb + 60.0) / 60.0, 0, 1);
    }

    private static IReadOnlyDictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                result[args[i]] = args[i + 1];
                i++;
            }
            else
            {
                result[args[i]] = string.Empty;
            }
        }

        return result;
    }
}
