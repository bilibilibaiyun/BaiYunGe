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
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "BaiYunGe";

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
    private System.Threading.Timer? _keepAliveTimer;
    private int _overlayHideGeneration;
    private UpdateChecker? _updateChecker;
    private UpdateInfo? _latestUpdateInfo;
    private bool _updateCheckCompleted;

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
        SetAutoStart(_settings.AutoStart);

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

        // 启动模型保活定时器（每隔 5 分钟健康检查，进程退出则自动重启）。
        StartKeepAliveTimer();

        // 模型就绪时后台预热 llama-server，消除首次识别的冷启动延迟（约 5 秒加载模型）。
        WarmupServerInBackground();

        // 首次运行自动弹设置，并直接定位到「模型」页引导下载。
        if (string.IsNullOrWhiteSpace(_settings.ModelDirectory) || args.ContainsKey("--settings"))
        {
            ShowSettings(focusModel: true);
        }
        else
        {
            // 正常静默启动：用不抢焦点的浮窗显示「已最小化到托盘」提示（托盘气泡易被系统通知设置吞掉）。
            _ = ShowStartupNoticeAsync();
        }

        // 后台静默检测新版本，结果缓存供设置页显示（打开设置页即可看到是否有新版）。
        _ = CheckUpdateSilentlyAsync();
    }

    /// <summary>静默更新检测完成时触发（供设置页刷新状态显示）。</summary>
    public event Action? UpdateCheckCompleted;

    /// <summary>启动时静默检测的最新版本信息（未完成或失败时为 null）。</summary>
    public UpdateInfo? LatestUpdateInfo => _latestUpdateInfo;

    /// <summary>是否已完成启动时的静默检测（失败也算完成）。</summary>
    public bool IsUpdateCheckCompleted => _updateCheckCompleted;

    /// <summary>后台静默检测一次 GitHub 最新版本，缓存结果并通知设置页。</summary>
    private async Task CheckUpdateSilentlyAsync()
    {
        try
        {
            _updateChecker ??= new UpdateChecker();
            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.1.2";
            _latestUpdateInfo = await _updateChecker.CheckAsync(current);
        }
        catch (Exception exception)
        {
            _logger?.Warn($"Silent update check failed: {exception.Message}");
        }
        finally
        {
            _updateCheckCompleted = true;
            UpdateCheckCompleted?.Invoke();
        }
    }

    /// <summary>启动后短暂显示「已最小化到托盘」提示，4 秒后自动消失。</summary>
    private async Task ShowStartupNoticeAsync()
    {
        try
        {
            await Task.Delay(600);
            await Dispatcher.InvokeAsync(() =>
            {
                ShowOverlay();
                _overlay!.ShowNotice(_text!.Get("Tray.StartupTipText"));
            });
            await Task.Delay(4000);
            await Dispatcher.InvokeAsync(() => _overlay?.Hide());
        }
        catch
        {
            // 提示失败不影响运行。
        }
    }

    /// <summary>
    /// 后台预热 llama-server：模型目录已配置且确有 gguf 文件时，
    /// 在状态浮窗显示「模型预热中」，把模型加载进显存/内存后自动隐藏。
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
                // 延迟预热：等软件启动稳定（托盘就绪、UI 响应）后再加载模型，
                // 避免启动早期（尤其更新覆盖安装后）模型大文件磁盘 I/O 与 GPU 探测
                // 抢占资源，导致自启动缓慢、界面卡顿。首次识别若早于此时仍会走懒启动。
                await Task.Delay(TimeSpan.FromSeconds(15));

                // 显示预热提示（不抢焦点浮窗，纯文字无电平条）。
                await Dispatcher.InvokeAsync(() =>
                {
                    ShowOverlay();
                    _overlay!.ShowNotice(_text!.Get("Overlay.WarmingUp"));
                });

                await _server!.EnsureStartedAsync(modelDir, _settings.InferenceDevice, CancellationToken.None);
                _logger!.Info("llama-server warmed up.");
            }
            catch (Exception exception)
            {
                _logger!.Warn($"Model warmup skipped: {exception.Message}");
            }
            finally
            {
                // 仅在空闲（非识别中）时隐藏预热提示，避免误关识别中的状态弹窗。
                if (_pipeline!.CurrentStage == PipelineStage.Idle)
                {
                    await Dispatcher.InvokeAsync(() => _overlay?.Hide());
                }
            }
        });
    }

    /// <summary>启动模型保活定时器：每隔一段时间健康检查 llama-server，退出则自动重启。</summary>
    private void StartKeepAliveTimer()
    {
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = new System.Threading.Timer(
            _ => _ = KeepModelAliveAsync(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    private async Task KeepModelAliveAsync()
    {
        var modelDir = _settings!.ModelDirectory;
        if (string.IsNullOrWhiteSpace(modelDir) || !Directory.Exists(modelDir))
        {
            return;
        }

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

        // llama-server 进程意外退出后自动重启，保持模型常驻。
        if (!_server!.IsHealthy)
        {
            _logger!.Info("llama-server not healthy; restarting keep-alive warmup.");
            try
            {
                await _server.EnsureStartedAsync(modelDir, _settings.InferenceDevice, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _logger!.Warn($"Keep-alive warmup failed: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// 将「开机自启动」设置同步到 HKCU Run 注册表项。
    /// 勾选时写入当前可执行文件路径，取消时删除该值；失败静默降级，不影响主流程。
    /// </summary>
    private void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                _logger?.Warn("Auto-start registry key not found.");
                return;
            }

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(RunValueName, $"\"{exePath}\"");
                    _logger?.Info($"Auto-start enabled: {exePath}");
                }
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                _logger?.Info("Auto-start disabled.");
            }
        }
        catch (Exception exception)
        {
            _logger?.Warn($"Failed to update auto-start registry: {exception.Message}");
        }
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
        if (_settings!.KeyboardMode == "hold" && _pipeline!.CurrentStage == PipelineStage.Listening)
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

            _ = HideOverlayAfterDelayAsync(_overlayHideGeneration);
        });
    }

    private void OnPipelineError(object? sender, string message)
    {
        Dispatcher.Invoke(() => _overlay?.ShowMessage($"{_text!.Get("Overlay.Error")}: {message}"));
    }

    /// <summary>延迟隐藏浮窗。带代次校验：若期间开始了新的识别，则放弃隐藏。</summary>
    private async Task HideOverlayAfterDelayAsync(int generation)
    {
        await Task.Delay(1200);
        Dispatcher.Invoke(() =>
        {
            if (generation == _overlayHideGeneration)
            {
                _overlay?.Hide();
            }
        });
    }

    private void ShowOverlay()
    {
        _overlay ??= new OverlayWindow();
        // 新识别开始：递增代次，使上一次识别遗留的「延迟隐藏」定时器失效。
        _overlayHideGeneration++;
        _overlay.ShowMessage(_text!.Get("Overlay.Listening"));
        if (!_overlay.IsVisible)
        {
            _overlay.Show();
        }
    }

    private void ShowSettings(bool focusModel = false)
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
                SetAutoStart(_settings.AutoStart);
            };
            _mainWindow.ModelInstalled += async () =>
            {
                // 模型替换后重启推理服务，并立即预热新模型，消除首次识别冷启动。
                await _server!.StopAsync();
                WarmupServerInBackground();
            };
        }

        if (focusModel)
        {
            _mainWindow.NavigateToModel();
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("BaiYunGe exiting.");
        _keepAliveTimer?.Dispose();
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
