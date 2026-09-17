using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BaiYunGe.Core;
using BaiYunGe.Core.Audio;
using BaiYunGe.Core.Inference;
using BaiYunGe.Core.Keyboard;

namespace BaiYunGe.UI;

public partial class MainWindow : Window
{
    private readonly LocalizedText _text;
    private readonly AppSettings _settings;
    private readonly AppSettingsStore _store;
    private readonly AudioCaptureService _audioCapture;
    private readonly KeyboardHotkeyService _hotkeyService;
    private readonly ModelDownloader _downloader;
    private readonly AppLogger _logger;
    private readonly UpdateChecker _updateChecker = new();
    private readonly ReleaseCatalog _releaseCatalog = new();

    private string _currentPage = "general";

    // 页面控件引用
    private ComboBox? _languageCombo;
    private ComboBox? _themeCombo;
    private ComboBox? _micCombo;
    private ComboBox? _deviceCombo;
    private ComboBox? _recognitionLangCombo;
    private ComboBox? _noiseEnvCombo;
    private ComboBox? _gainCombo;
    private TextBlock? _calibrationStatus;
    private StackPanel? _calibrationActions;
    private Button? _clearCalibrationButton;
    private double _pendingCalibrationThreshold;
    private TextBox? _shortcutBox;
    private ComboBox? _modeCombo;
    private CheckBox? _dictEnabled;
    private ListView? _dictList;
    private TextBox? _aliasBox;
    private TextBox? _targetBox;
    private TextBox? _modelDirBox;
    private ProgressBar? _modelProgress;
    private TextBlock? _modelStatus;
    private TextBlock? _updateStatus;
    private TextBlock? _updateNotes;
    private Button? _updateButton;
    private CheckBox? _autoCheckUpdate;
    private ProgressBar? _updateProgress;
    private string? _pendingInstallUrl;
    private string? _pendingInstallVersion;

    public MainWindow(
        LocalizedText text,
        AppSettings settings,
        AppSettingsStore store,
        AudioCaptureService audioCapture,
        KeyboardHotkeyService hotkeyService,
        ModelDownloader downloader,
        AppLogger logger)
    {
        InitializeComponent();
        _text = text;
        _settings = settings;
        _store = store;
        _audioCapture = audioCapture;
        _hotkeyService = hotkeyService;
        _downloader = downloader;
        _logger = logger;

        _hotkeyService.HotkeyRecorded += OnHotkeyRecorded;
        _hotkeyService.RecordCancelled += OnRecordCancelled;
        text.LanguageChanged += (_, _) => RefreshLocalization();

        // 订阅启动时静默检测完成事件，及时刷新更新状态显示。
        if (System.Windows.Application.Current is App app)
        {
            app.UpdateCheckCompleted += OnUpdateCheckCompleted;
        }

        RefreshLocalization();
        ShowPage("general");
    }

    public event Action? SettingsChanged;

    public event Action? ModelInstalled;

    public event Func<Task>? ModelReplacing;

    private void RefreshLocalization()
    {
        Title = _text.Get("Settings.Title");
        NavGeneral.Content = _text.Get("Page.General");
        NavShortcut.Content = _text.Get("Page.Shortcut");
        NavDictionary.Content = _text.Get("Page.Dictionary");
        NavModel.Content = _text.Get("Page.Model");
        NavCalibration.Content = _text.Get("Page.Calibration");
        NavAbout.Content = _text.Get("Page.About");
        ShowPage(_currentPage);
    }

    private void ShowPage(string page)
    {
        _currentPage = page;
        UpdateNavHighlight(page);
        ContentHost.Children.Clear();
        switch (page)
        {
            case "general":
                ContentHost.Children.Add(BuildGeneralPage());
                break;
            case "shortcut":
                ContentHost.Children.Add(BuildShortcutPage());
                break;
            case "dictionary":
                ContentHost.Children.Add(BuildDictionaryPage());
                break;
            case "model":
                ContentHost.Children.Add(BuildModelPage());
                break;
            case "calibration":
                ContentHost.Children.Add(BuildCalibrationPage());
                break;
            case "about":
                ContentHost.Children.Add(BuildAboutPage());
                break;
        }
    }

    private void UpdateNavHighlight(string page)
    {
        var accent = (System.Windows.Media.Brush)Application.Current.Resources["Theme.Accent"];
        var white = System.Windows.Media.Brushes.White;
        var textSecondary = (System.Windows.Media.Brush)Application.Current.Resources["Theme.TextSecondary"];
        var transparent = System.Windows.Media.Brushes.Transparent;

        void Apply(Button btn, bool active)
        {
            btn.Background = active ? accent : transparent;
            btn.Foreground = active ? white : textSecondary;
        }

        Apply(NavGeneral, page == "general");
        Apply(NavShortcut, page == "shortcut");
        Apply(NavDictionary, page == "dictionary");
        Apply(NavModel, page == "model");
        Apply(NavCalibration, page == "calibration");
        Apply(NavAbout, page == "about");
    }

    /// <summary>首次运行引导：直接定位到「模型」页，引导用户下载模型。</summary>
    public void NavigateToModel()
    {
        ShowPage("model");
    }

    private UIElement BuildGeneralPage()
    {
        var panel = new StackPanel();

        panel.Children.Add(Label(_text.Get("General.Language")));
        _languageCombo = new ComboBox();
        _languageCombo.Items.Add(new ComboBoxItem { Content = "简体中文", Tag = "zh-CN" });
        _languageCombo.Items.Add(new ComboBoxItem { Content = "English", Tag = "en-US" });
        _languageCombo.SelectionChanged += (_, _) =>
        {
            if (_languageCombo.SelectedItem is ComboBoxItem { Tag: string lang } &&
                _settings.Language != lang)
            {
                _settings.Language = lang;
                _text.SetLanguage(lang == "en-US" ? AppLanguage.EnUs : AppLanguage.ZhCn);
                Save();
            }
        };
        SelectCombo(_languageCombo, _settings.Language);
        panel.Children.Add(_languageCombo);

        panel.Children.Add(Label(_text.Get("General.Theme")));
        _themeCombo = new ComboBox();
        _themeCombo.Items.Add(new ComboBoxItem { Content = _text.Get("General.ThemeDark"), Tag = "dark" });
        _themeCombo.Items.Add(new ComboBoxItem { Content = _text.Get("General.ThemeLight"), Tag = "light" });
        _themeCombo.SelectionChanged += (_, _) =>
        {
            if (_themeCombo.SelectedItem is ComboBoxItem { Tag: string theme } && _settings.Theme != theme)
            {
                _settings.Theme = theme;
                Save();
                SettingsChanged?.Invoke();
            }
        };
        SelectCombo(_themeCombo, _settings.Theme);
        panel.Children.Add(_themeCombo);

        panel.Children.Add(Label(_text.Get("General.Device")));
        _deviceCombo = new ComboBox();
        _deviceCombo.Items.Add(new ComboBoxItem { Content = _text.Get("General.DeviceGpu"), Tag = "gpu" });
        _deviceCombo.Items.Add(new ComboBoxItem { Content = _text.Get("General.DeviceCpu"), Tag = "cpu" });
        _deviceCombo.SelectionChanged += (_, _) =>
        {
            if (_deviceCombo.SelectedItem is ComboBoxItem { Tag: string device } && _settings.InferenceDevice != device)
            {
                _settings.InferenceDevice = device;
                Save();
            }
        };
        SelectCombo(_deviceCombo, _settings.InferenceDevice);
        panel.Children.Add(_deviceCombo);

        panel.Children.Add(Label(_text.Get("General.RecognitionLanguage")));
        _recognitionLangCombo = new ComboBox();
        _recognitionLangCombo.Items.Add(new ComboBoxItem { Content = _text.Get("General.LangAuto"), Tag = "auto" });
        _recognitionLangCombo.Items.Add(new ComboBoxItem { Content = _text.Get("General.LangZh"), Tag = "zh" });
        _recognitionLangCombo.Items.Add(new ComboBoxItem { Content = _text.Get("General.LangEn"), Tag = "en" });
        _recognitionLangCombo.SelectionChanged += (_, _) =>
        {
            if (_recognitionLangCombo.SelectedItem is ComboBoxItem { Tag: string lang } &&
                _settings.RecognitionLanguage != lang)
            {
                _settings.RecognitionLanguage = lang;
                Save();
            }
        };
        SelectCombo(_recognitionLangCombo, _settings.RecognitionLanguage);
        panel.Children.Add(_recognitionLangCombo);

        panel.Children.Add(Label(_text.Get("General.Mic")));
        _micCombo = new ComboBox();
        RefreshMicList();
        _micCombo.SelectionChanged += (_, _) =>
        {
            if (_micCombo.SelectedItem is ComboBoxItem { Tag: string id } && _settings.MicDeviceId != id)
            {
                _settings.MicDeviceId = id;
                Save();
            }
        };
        panel.Children.Add(_micCombo);

        var testButton = new Button { Content = _text.Get("General.TestMic") };
        testButton.Click += async (_, _) => await TestMicrophoneAsync();
        panel.Children.Add(testButton);

        var autoStart = new CheckBox { Content = _text.Get("General.AutoStart"), IsChecked = _settings.AutoStart };
        autoStart.Checked += (_, _) => { _settings.AutoStart = true; Save(); SettingsChanged?.Invoke(); };
        autoStart.Unchecked += (_, _) => { _settings.AutoStart = false; Save(); SettingsChanged?.Invoke(); };
        panel.Children.Add(autoStart);

        return panel;
    }

    private UIElement BuildShortcutPage()
    {
        var panel = new StackPanel();

        panel.Children.Add(Label(_text.Get("Shortcut.Current")));
        _shortcutBox = new TextBox { IsReadOnly = true, Text = _hotkeyService.CurrentHotkey.DisplayText };
        panel.Children.Add(_shortcutBox);

        var recordButton = new Button { Content = _text.Get("Shortcut.Record") };
        recordButton.Click += (_, _) =>
        {
            _shortcutBox.Text = _text.Get("Shortcut.Recording");
            _hotkeyService.BeginRecord();
        };
        panel.Children.Add(recordButton);

        panel.Children.Add(Label(_text.Get("Shortcut.Mode")));
        _modeCombo = new ComboBox();
        _modeCombo.Items.Add(new ComboBoxItem { Content = _text.Get("Shortcut.ModeHold"), Tag = "hold" });
        _modeCombo.Items.Add(new ComboBoxItem { Content = _text.Get("Shortcut.ModeToggle"), Tag = "toggle" });
        _modeCombo.SelectionChanged += (_, _) =>
        {
            if (_modeCombo.SelectedItem is ComboBoxItem { Tag: string mode } && _settings.KeyboardMode != mode)
            {
                _settings.KeyboardMode = mode;
                Save();
            }
        };
        SelectCombo(_modeCombo, _settings.KeyboardMode);
        panel.Children.Add(_modeCombo);

        return panel;
    }

    private UIElement BuildDictionaryPage()
    {
        var panel = new StackPanel();

        _dictEnabled = new CheckBox { Content = _text.Get("Dict.Enabled"), IsChecked = _settings.DictionaryEnabled };
        _dictEnabled.Checked += (_, _) => { _settings.DictionaryEnabled = true; Save(); };
        _dictEnabled.Unchecked += (_, _) => { _settings.DictionaryEnabled = false; Save(); };
        panel.Children.Add(_dictEnabled);

        _dictList = new ListView { Height = 220, Margin = new Thickness(0, 10, 0, 0) };
        var gridView = new GridView();
        gridView.Columns.Add(new GridViewColumn { Header = _text.Get("Dict.Alias"), Width = 180, DisplayMemberBinding = new System.Windows.Data.Binding("Alias") });
        gridView.Columns.Add(new GridViewColumn { Header = _text.Get("Dict.Target"), Width = 220, DisplayMemberBinding = new System.Windows.Data.Binding("Target") });
        _dictList.View = gridView;
        RefreshDictList();
        panel.Children.Add(_dictList);

        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _aliasBox = new TextBox { Margin = new Thickness(0, 4, 4, 0) };
        _targetBox = new TextBox { Margin = new Thickness(4, 4, 4, 0) };
        Grid.SetColumn(_aliasBox, 0);
        Grid.SetColumn(_targetBox, 1);
        inputGrid.Children.Add(_aliasBox);
        inputGrid.Children.Add(_targetBox);

        var addButton = new Button { Content = _text.Get("Dict.Add"), Margin = new Thickness(4, 4, 0, 0) };
        addButton.Click += (_, _) =>
        {
            var alias = _aliasBox.Text.Trim();
            var target = _targetBox.Text.Trim();
            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(target))
            {
                return;
            }

            _settings.Dictionary.Add(new DictionaryEntry { Alias = alias, Target = target });
            _aliasBox.Text = string.Empty;
            _targetBox.Text = string.Empty;
            RefreshDictList();
            Save();
        };
        Grid.SetColumn(addButton, 2);
        inputGrid.Children.Add(addButton);
        panel.Children.Add(inputGrid);

        var removeButton = new Button { Content = _text.Get("Dict.Remove") };
        removeButton.Click += (_, _) =>
        {
            if (_dictList.SelectedItem is DictionaryEntry entry)
            {
                _settings.Dictionary.Remove(entry);
                RefreshDictList();
                Save();
            }
        };
        panel.Children.Add(removeButton);

        var recordButton = new Button { Content = _text.Get("Dict.RecordVoice"), Margin = new Thickness(0, 4, 0, 0) };
        recordButton.Click += async (_, _) =>
        {
            if (_dictList.SelectedItem is DictionaryEntry entry)
            {
                await RecordVoiceTemplateAsync(entry);
            }
            else
            {
                MessageBox.Show(this, _text.Get("Dict.SelectFirst"),
                    _text.Get("Dict.RecordVoice"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        panel.Children.Add(recordButton);

        return panel;
    }

    /// <summary>录音念词：录下用户念该词的音频，提取 MFCC 模板保存，供识别时声纹比对。</summary>
    private async Task RecordVoiceTemplateAsync(DictionaryEntry entry)
    {
        var tempWav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"baiyunge-voice-{Guid.NewGuid():N}.wav");
        using var capture = new AudioCaptureService();
        try
        {
            MessageBox.Show(this, string.Format(_text.Get("Dict.RecordPrompt"), entry.Target),
                _text.Get("Dict.RecordVoice"), MessageBoxButton.OK, MessageBoxImage.Information);

            // 显示「录音中」非模态提示，让用户知道正在录音。
            var recordingWindow = new Window
            {
                Title = _text.Get("Dict.RecordVoice"),
                Width = 340,
                Height = 130,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false,
                Content = new TextBlock
                {
                    Text = string.Format(_text.Get("Dict.Recording"), entry.Target),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(24, 16, 24, 16),
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
            recordingWindow.Show();
            try
            {
                // 录 2 秒（静音不停止）。
                await capture.StartAsync(
                    _settings.MicDeviceId, tempWav, 2, 0,
                    _settings.VadSensitivity, _settings.NoiseEnvironment, _settings.InputGainDb,
                    _settings.CalibratedThresholdDb, CancellationToken.None);
            }
            finally
            {
                recordingWindow.Close();
            }

            var samples = MfccExtractor.ReadWav(tempWav);
            var mfcc = MfccExtractor.Extract(samples);
            if (mfcc.Length == 0)
            {
                MessageBox.Show(this, _text.Get("Dict.RecordFailed"),
                    _text.Get("Dict.RecordVoice"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var templatePath = GetVoiceTemplatePath(entry);
            VoiceTemplateStore.Save(templatePath, mfcc);

            var index = _settings.Dictionary.IndexOf(entry);
            if (index >= 0)
            {
                _settings.Dictionary[index] = entry with { VoicePath = templatePath };
            }

            Save();
            RefreshDictList();
            MessageBox.Show(this, _text.Get("Dict.RecordDone"),
                _text.Get("Dict.RecordVoice"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message,
                _text.Get("Dict.RecordVoice"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tempWav))
                {
                    System.IO.File.Delete(tempWav);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>生成词的录音模板路径（按 Target+Alias 哈希，存数据目录 VoiceTemplates 下）。</summary>
    private static string GetVoiceTemplatePath(DictionaryEntry entry)
    {
        var dir = System.IO.Path.Combine(AppPaths.DefaultDataRoot, "VoiceTemplates");
        Directory.CreateDirectory(dir);
        var key = entry.Target + "|" + entry.Alias;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return System.IO.Path.Combine(dir, hash + ".json");
    }

    private UIElement BuildCalibrationPage()
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = _text.Get("Calibration.Hint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Theme.TextSecondary"]
        });

        panel.Children.Add(Label(_text.Get("General.NoiseEnv")));
        _noiseEnvCombo = new ComboBox();
        _noiseEnvCombo.Items.Add(new ComboBoxItem { Content = _text.Get("General.NoiseAuto"), Tag = "auto" });
        _noiseEnvCombo.Items.Add(new ComboBoxItem { Content = _text.Get("General.NoiseQuiet"), Tag = "quiet" });
        _noiseEnvCombo.Items.Add(new ComboBoxItem { Content = _text.Get("General.NoiseNoisy"), Tag = "noisy" });
        _noiseEnvCombo.SelectionChanged += (_, _) =>
        {
            if (_noiseEnvCombo.SelectedItem is ComboBoxItem { Tag: string env } && _settings.NoiseEnvironment != env)
            {
                _settings.NoiseEnvironment = env;
                Save();
            }
        };
        SelectCombo(_noiseEnvCombo, _settings.NoiseEnvironment);
        panel.Children.Add(_noiseEnvCombo);

        panel.Children.Add(Label(_text.Get("General.InputGain")));
        _gainCombo = new ComboBox();
        foreach (var g in new[] { 0, 6, 12, 18 })
        {
            _gainCombo.Items.Add(new ComboBoxItem { Content = g == 0 ? _text.Get("General.GainOff") : $"+{g} dB", Tag = g.ToString() });
        }
        _gainCombo.SelectionChanged += (_, _) =>
        {
            if (_gainCombo.SelectedItem is ComboBoxItem { Tag: string gainStr } &&
                int.TryParse(gainStr, out var gain) && _settings.InputGainDb != gain)
            {
                _settings.InputGainDb = gain;
                Save();
            }
        };
        SelectCombo(_gainCombo, _settings.InputGainDb.ToString());
        panel.Children.Add(_gainCombo);

        var startButton = new Button { Content = _text.Get("Calibration.Start") };
        startButton.Click += async (_, _) => await CalibrateEnvironmentAsync();
        panel.Children.Add(startButton);

        _calibrationStatus = new TextBlock
        {
            Text = string.Empty,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0)
        };
        panel.Children.Add(_calibrationStatus);

        _calibrationActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        var useButton = new Button { Content = _text.Get("Calibration.Use"), Width = 100 };
        var skipButton = new Button { Content = _text.Get("Calibration.Skip"), Width = 100, Margin = new Thickness(8, 0, 0, 0) };
        var redoButton = new Button { Content = _text.Get("Calibration.Redo"), Width = 100, Margin = new Thickness(8, 0, 0, 0) };
        useButton.Click += (_, _) =>
        {
            if (_pendingCalibrationThreshold < 0)
            {
                _settings.CalibratedThresholdDb = _pendingCalibrationThreshold;
                Save();
            }
            _pendingCalibrationThreshold = 0;
            ShowPage("calibration");
        };
        skipButton.Click += (_, _) =>
        {
            _settings.CalibratedThresholdDb = 0;
            Save();
            _pendingCalibrationThreshold = 0;
            ShowPage("calibration");
        };
        redoButton.Click += async (_, _) => await CalibrateEnvironmentAsync();
        _calibrationActions.Children.Add(useButton);
        _calibrationActions.Children.Add(skipButton);
        _calibrationActions.Children.Add(redoButton);
        panel.Children.Add(_calibrationActions);

        _clearCalibrationButton = new Button
        {
            Content = _text.Get("Calibration.Clear"),
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        _clearCalibrationButton.Click += (_, _) =>
        {
            _settings.CalibratedThresholdDb = 0;
            Save();
            _pendingCalibrationThreshold = 0;
            ShowPage("calibration");
        };
        panel.Children.Add(_clearCalibrationButton);

        UpdateCalibrationStatus();

        return panel;
    }

    private void UpdateCalibrationStatus()
    {
        if (_calibrationStatus is null)
        {
            return;
        }

        if (_pendingCalibrationThreshold < 0)
        {
            _calibrationStatus.Text = string.Format(_text.Get("Calibration.PendingUse"), _pendingCalibrationThreshold);
            if (_calibrationActions is not null)
            {
                _calibrationActions.Visibility = Visibility.Visible;
            }
            if (_clearCalibrationButton is not null)
            {
                _clearCalibrationButton.Visibility = Visibility.Collapsed;
            }
        }
        else if (_settings.CalibratedThresholdDb < 0)
        {
            _calibrationStatus.Text = string.Format(_text.Get("Calibration.Current"), _settings.CalibratedThresholdDb);
            if (_calibrationActions is not null)
            {
                _calibrationActions.Visibility = Visibility.Collapsed;
            }
            if (_clearCalibrationButton is not null)
            {
                _clearCalibrationButton.Visibility = Visibility.Visible;
            }
        }
        else
        {
            _calibrationStatus.Text = _text.Get("Calibration.None");
            if (_calibrationActions is not null)
            {
                _calibrationActions.Visibility = Visibility.Collapsed;
            }
            if (_clearCalibrationButton is not null)
            {
                _clearCalibrationButton.Visibility = Visibility.Collapsed;
            }
        }
    }

    private UIElement BuildAboutPage()
    {
        var panel = new StackPanel();

        panel.Children.Add(Label(_text.Get("Update.CurrentVersion")));
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "3.2.0";
        panel.Children.Add(new TextBlock { Text = version, FontSize = 14, Margin = new Thickness(0, 4, 0, 0) });

        var checkButton = new Button { Content = _text.Get("Update.Check") };
        checkButton.Click += async (_, _) => await CheckForUpdateAsync();
        panel.Children.Add(checkButton);

        _autoCheckUpdate = new CheckBox
        {
            Content = _text.Get("Update.AutoCheck"),
            IsChecked = _settings.AutoCheckUpdate,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _autoCheckUpdate.Checked += (_, _) => { _settings.AutoCheckUpdate = true; Save(); };
        _autoCheckUpdate.Unchecked += (_, _) => { _settings.AutoCheckUpdate = false; Save(); };
        panel.Children.Add(_autoCheckUpdate);

        _updateStatus = new TextBlock
        {
            Text = string.Empty,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(_updateStatus);

        _updateNotes = new TextBlock
        {
            Text = string.Empty,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        panel.Children.Add(_updateNotes);

        _updateProgress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 6,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        panel.Children.Add(_updateProgress);

        _updateButton = new Button
        {
            Content = string.Empty,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _updateButton.Click += async (_, _) =>
        {
            if (_pendingInstallUrl is not null && _pendingInstallVersion is not null)
            {
                var url = _pendingInstallUrl;
                var ver = _pendingInstallVersion;
                _pendingInstallUrl = null;
                _pendingInstallVersion = null;
                await DownloadAndInstallUpdateAsync(url, ver);
            }
            else if (System.Windows.Application.Current is App { LatestUpdateInfo: { HasUpdate: true } info } &&
                !string.IsNullOrWhiteSpace(info.DownloadUrl))
            {
                await DownloadAndInstallUpdateAsync(info.DownloadUrl, info.LatestVersion);
            }
        };
        panel.Children.Add(_updateButton);

        var selectVersionButton = new Button
        {
            Content = _text.Get("Update.SelectVersion"),
            Margin = new Thickness(0, 8, 0, 0)
        };
        selectVersionButton.Click += async (_, _) => await SelectVersionAsync();
        panel.Children.Add(selectVersionButton);

        RefreshUpdateStatus();

        return panel;
    }

    private async Task CalibrateEnvironmentAsync()
    {
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"baiyunge-calib-{Guid.NewGuid():N}.wav");
        // 用独立录音实例：采样期间的静音/设备停止不触发主流程的错误浮窗。
        using var calibrationCapture = new AudioCaptureService();
        try
        {
            MessageBox.Show(this, _text.Get("Calibration.Prompt"), _text.Get("Page.Calibration"),
                MessageBoxButton.OK, MessageBoxImage.Information);

            // 显示「录音中」非模态提示，让用户知道正在采样。
            var recordingWindow = BuildCalibrationRecordingWindow();
            recordingWindow.Show();
            try
            {
                // 录 5 秒基准音频（静音自动停止关闭，录满 5 秒）。
                await calibrationCapture.StartAsync(
                    _settings.MicDeviceId,
                    tempPath,
                    5,
                    0,
                    _settings.VadSensitivity,
                    _settings.NoiseEnvironment,
                    _settings.InputGainDb,
                    0,
                    CancellationToken.None);
            }
            finally
            {
                recordingWindow.Close();
            }

            var threshold = EnvironmentCalibrator.AnalyzeThreshold(tempPath);
            if (threshold >= 0)
            {
                MessageBox.Show(this, _text.Get("Calibration.Failed"), _text.Get("Page.Calibration"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 暂存采样结果，刷新页面显示「是否使用当前采样」的选择，不立即保存、不弹窗。
            _pendingCalibrationThreshold = threshold;
            ShowPage("calibration");
        }
        catch (Exception exception)
        {
            _logger.Error("Calibration failed.", exception);
            MessageBox.Show(this, _text.Get("Calibration.Failed"), _text.Get("Page.Calibration"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
            catch
            {
            }
        }
    }

    private Window BuildCalibrationRecordingWindow()
    {
        return new Window
        {
            Title = _text.Get("Page.Calibration"),
            Width = 340,
            Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Content = new TextBlock
            {
                Text = _text.Get("Calibration.Recording"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24, 16, 24, 16),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private UIElement BuildModelPage()
    {
        var panel = new StackPanel();

        panel.Children.Add(Label(_text.Get("Model.Directory")));
        var dirGrid = new Grid();
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _modelDirBox = new TextBox
        {
            Text = string.IsNullOrEmpty(_settings.ModelDirectory) ? AppPaths.DefaultModelDirectory : _settings.ModelDirectory,
            Margin = new Thickness(0, 4, 4, 0)
        };
        Grid.SetColumn(_modelDirBox, 0);
        dirGrid.Children.Add(_modelDirBox);

        var browseButton = new Button { Content = _text.Get("Model.Browse"), Margin = new Thickness(4, 4, 0, 0) };
        browseButton.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = _text.Get("Model.Directory") };
            if (dialog.ShowDialog(this) == true)
            {
                _modelDirBox.Text = dialog.FolderName;
                _settings.ModelDirectory = dialog.FolderName;
                Save();
            }
        };
        Grid.SetColumn(browseButton, 1);
        dirGrid.Children.Add(browseButton);
        panel.Children.Add(dirGrid);

        _modelStatus = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(_modelStatus);

        _modelProgress = new ProgressBar { Height = 6, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(_modelProgress);

        var installButton = new Button { Content = _text.Get("Model.Install") };
        installButton.Click += async (_, _) => await InstallModelAsync();
        panel.Children.Add(installButton);

        _ = UpdateModelStatusAsync();
        return panel;
    }

    private async Task InstallModelAsync()
    {
        var directory = _modelDirBox?.Text.Trim();
        if (string.IsNullOrEmpty(directory))
        {
            // 目录为空：默认使用软件安装路径下的 models 目录（不再静默返回）。
            directory = AppPaths.DefaultModelDirectory;
        }

        // 统一可写性检查：目录不可写（典型：安装在 Program Files 且普通权限运行）时回退到用户数据目录。
        directory = ResolveWritableModelDirectory(directory);
        _modelDirBox!.Text = directory;
        _settings.ModelDirectory = directory;
        Save();

        try
        {
            _modelProgress!.Visibility = Visibility.Visible;
            _modelStatus!.Text = _text.Get("Model.Downloading");

            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                if (!string.IsNullOrEmpty(p.Error))
                {
                    _modelStatus.Text = $"{p.FileName}: {p.Error}";
                    return;
                }

                if (p.TotalBytes > 0)
                {
                    _modelProgress.Value = (double)p.ReceivedBytes / p.TotalBytes * 100;
                    // 校验阶段（下载完成后 SHA256 完整性校验）单独提示，避免进度条停在 100% 看似卡住。
                    if (string.Equals(p.Source, "verifying", StringComparison.OrdinalIgnoreCase))
                    {
                        _modelStatus.Text = _text.Format("Model.Verifying", p.FileName, BytesToText(p.ReceivedBytes), BytesToText(p.TotalBytes));
                    }
                    else
                    {
                        _modelStatus.Text = _text.Format("Model.Progress", p.FileName, BytesToText(p.ReceivedBytes), BytesToText(p.TotalBytes));
                    }
                }
            });

            var summary = await _downloader.DownloadAsync(
                directory,
                beforeFileReplaceAsync: () => ModelReplacing?.Invoke() ?? Task.CompletedTask,
                progress,
                CancellationToken.None);

            _modelProgress.Visibility = Visibility.Collapsed;
            _modelStatus.Text = summary.IsComplete
                ? _text.Get("Model.Installed")
                : _text.Get("Model.NotInstalled");

            if (summary.IsComplete)
            {
                ModelInstalled?.Invoke();
            }
        }
        catch (Exception exception)
        {
            _modelProgress!.Visibility = Visibility.Collapsed;
            _modelStatus!.Text = exception.Message;
            _logger.Error("Model installation failed.", exception);
        }
    }

    /// <summary>
    /// 解析可写的模型目录：优先软件安装路径下的 models 子目录；
    /// 若不可写（典型：安装在 Program Files 且以普通权限运行），回退到用户数据目录，
    /// 保证「点安装模型」始终有可落盘的目录，不再静默失败。
    /// </summary>
    private static string ResolveWritableModelDirectory(string requested)
    {
        try
        {
            Directory.CreateDirectory(requested);
            var probe = Path.Combine(requested, ".write-probe");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return requested;
        }
        catch
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BaiYunGe",
                "Models");
            try
            {
                Directory.CreateDirectory(fallback);
            }
            catch
            {
                // 极端情况：连 LOCALAPPDATA 都不可写，返回原目录由下载逻辑兜底报错。
                return requested;
            }

            return fallback;
        }
    }

    private async Task UpdateModelStatusAsync()
    {
        var directory = _settings.ModelDirectory;
        if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
        {
            _modelStatus!.Text = _text.Get("Model.NotInstalled");
            return;
        }

        var summary = await _downloader.QuickVerifyAsync(directory);
        _modelStatus!.Text = summary.IsComplete ? _text.Get("Model.Installed") : _text.Get("Model.NotInstalled");
    }

    private async Task TestMicrophoneAsync()
    {
        var deviceId = _settings.MicDeviceId;
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"baiyunge-mic-test-{Guid.NewGuid():N}.wav");
        try
        {
            var result = await _audioCapture.StartAsync(
                deviceId,
                tempPath,
                3,
                1000,
                _settings.VadSensitivity,
                _settings.NoiseEnvironment,
                _settings.InputGainDb,
                _settings.CalibratedThresholdDb,
                CancellationToken.None);

            var text = result.HasSpeech
                ? $"RMS {result.PeakRmsDb:F1} dB"
                : $"RMS {result.PeakRmsDb:F1} dB (low)";
            MessageBox.Show(this, text, _text.Get("General.TestMic"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, _text.Get("General.TestMic"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
            catch
            {
            }
        }
    }

    private void RefreshMicList()
    {
        if (_micCombo is null)
        {
            return;
        }

        _micCombo.Items.Clear();
        var current = _settings.MicDeviceId;
        var found = false;
        foreach (var device in _audioCapture.GetDevices())
        {
            var item = new ComboBoxItem { Content = device.Name, Tag = device.Id };
            _micCombo.Items.Add(item);
            if (device.Id == current)
            {
                _micCombo.SelectedItem = item;
                found = true;
            }
        }

        if (!found && _micCombo.Items.Count > 0)
        {
            _micCombo.SelectedIndex = 0;
            _settings.MicDeviceId = ((ComboBoxItem)_micCombo.Items[0]!).Tag as string ?? string.Empty;
        }
    }

    private void RefreshDictList()
    {
        if (_dictList is not null)
        {
            _dictList.ItemsSource = null;
            _dictList.ItemsSource = _settings.Dictionary;
        }
    }

    private void OnHotkeyRecorded(HotkeyDefinition hotkey)
    {
        Dispatcher.Invoke(() =>
        {
            if (_shortcutBox is not null)
            {
                _shortcutBox.Text = hotkey.DisplayText;
            }

            _settings.KeyboardShortcut = hotkey.DisplayText;
            Save();
            SettingsChanged?.Invoke();
        });
    }

    private void OnRecordCancelled()
    {
        Dispatcher.Invoke(() =>
        {
            if (_shortcutBox is not null)
            {
                _shortcutBox.Text = _hotkeyService.CurrentHotkey.DisplayText;
            }
        });
    }

    private void Save()
    {
        _store.Save(_settings);
    }

    /// <summary>检查 GitHub 是否有新版本；有更新则显示更新日志并询问是否更新。</summary>
    private async Task CheckForUpdateAsync()
    {
        if (_updateStatus is null)
        {
            return;
        }

        // 优先使用启动时静默检测的缓存结果（已有新版则直接询问，无需重复检测）。
        UpdateInfo? info = null;
        if (System.Windows.Application.Current is App { IsUpdateCheckCompleted: true } app)
        {
            info = app.LatestUpdateInfo;
        }

        // 无缓存新版时（静默检测未完成/失败/无新版），用户主动触发则重新检测一次保证最新。
        if (info is null || !info.HasUpdate)
        {
            _updateStatus.Text = _text.Get("Update.Checking");
            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.4";
            info = await _updateChecker.CheckAsync(current);
        }

        if (info is null)
        {
            _updateStatus.Text = _text.Get("Update.Failed");
            return;
        }

        if (!info.HasUpdate)
        {
            _updateStatus.Text = _text.Get("Update.UpToDate");
            return;
        }

        // 界面内显示更新日志与「立即更新」按钮，不弹窗。自动检测只查稳定版，无需测试版风险提示。
        _updateStatus.Text = $"{_text.Get("Update.NewVersion")}：v{info.LatestVersion}";
        ShowUpdateNotes(info.ReleaseNotes);
        if (_updateButton is not null && !string.IsNullOrWhiteSpace(info.DownloadUrl))
        {
            _updateButton.Content = $"{_text.Get("Update.UpdateNow")} v{info.LatestVersion}";
            _updateButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>界面内显示更新日志（不弹窗）。空文本则隐藏；过长则截断。</summary>
    private void ShowUpdateNotes(string notes)
    {
        if (_updateNotes is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            _updateNotes.Visibility = Visibility.Collapsed;
            return;
        }

        var display = notes.Length > 800 ? notes[..800] + "…" : notes;
        _updateNotes.Text = display;
        _updateNotes.Visibility = Visibility.Visible;
    }

    /// <summary>根据启动时静默检测的缓存结果刷新更新状态显示。</summary>
    private void RefreshUpdateStatus()
    {
        var app = System.Windows.Application.Current as App;
        if (app is null)
        {
            return;
        }

        // 红点独立于「关于」页面是否已构建：检测到新稳定版且该版本尚未提示过，就显示红点。
        if (app.IsUpdateCheckCompleted && app.LatestUpdateInfo is { HasUpdate: true } info &&
            !string.Equals(info.LatestVersion, _settings.LastNotifiedVersion, StringComparison.OrdinalIgnoreCase))
        {
            ShowUpdateBadge();
        }

        // 状态文本与更新按钮（依赖「关于」页面控件）。
        if (_updateStatus is null)
        {
            return;
        }

        if (!app.IsUpdateCheckCompleted)
        {
            _updateStatus.Text = _text.Get("Update.Checking");
            HideUpdateButton();
        }
        else if (app.LatestUpdateInfo is { HasUpdate: true })
        {
            var updateInfo = app.LatestUpdateInfo;
            _updateStatus.Text = $"{_text.Get("Update.NewVersion")}：v{updateInfo.LatestVersion}";
            ShowUpdateNotes(updateInfo.ReleaseNotes);
            if (_updateButton is not null)
            {
                _updateButton.Content = $"{_text.Get("Update.UpdateNow")} v{updateInfo.LatestVersion}";
                _updateButton.Visibility = Visibility.Visible;
            }
        }
        else if (app.LatestUpdateInfo is not null)
        {
            _updateStatus.Text = _text.Get("Update.UpToDate");
            HideUpdateButton();
        }
        else
        {
            _updateStatus.Text = _text.Get("Update.Failed");
            HideUpdateButton();
        }
    }

    private void ShowUpdateBadge()
    {
        NavAboutBadge.Visibility = Visibility.Visible;
    }

    private void HideUpdateBadge()
    {
        NavAboutBadge.Visibility = Visibility.Collapsed;
        // 记录当前已提示的版本号，使该版本的红点不再回归。
        if (System.Windows.Application.Current is App { LatestUpdateInfo: { HasUpdate: true } info })
        {
            _settings.LastNotifiedVersion = info.LatestVersion;
            Save();
        }
    }

    /// <summary>隐藏「立即更新」按钮（检测中/无新版/失败时）。</summary>
    private void HideUpdateButton()
    {
        if (_updateButton is not null)
        {
            _updateButton.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>静默检测完成（后台线程）后，切回 UI 线程刷新状态。</summary>
    private void OnUpdateCheckCompleted()
    {
        Dispatcher.Invoke(RefreshUpdateStatus);
    }

    /// <summary>选择版本：拉取历史版本列表（含测试版），用户选择后下载对应安装包覆盖安装。</summary>
    private async Task SelectVersionAsync()
    {
        try
        {
            _updateStatus!.Text = _text.Get("Update.Checking");
            var releases = await _releaseCatalog.GetReleasesAsync();
            if (releases is null || releases.Count == 0)
            {
                _updateStatus.Text = _text.Get("Update.Failed");
                return;
            }

            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;

            var selected = await ShowVersionPickerAsync(releases, current);
            if (selected is null)
            {
                _updateStatus.Text = _text.Get("Update.UpToDate");
                return;
            }

            _updateStatus.Text = _text.Get("Update.Checking");
            var url = await _releaseCatalog.GetDownloadUrlAsync(selected.Version);
            if (string.IsNullOrWhiteSpace(url))
            {
                _updateStatus.Text = _text.Get("Update.Failed");
                return;
            }

            // 界面内显示确认信息 + 「确认安装」按钮（不弹窗）。测试版在日志第一行提示风险。
            _pendingInstallUrl = url;
            _pendingInstallVersion = selected.Version;
            _updateStatus.Text = _text.Format("Update.InstallConfirm", selected.Version);
            ShowUpdateNotes(selected.IsStable ? string.Empty : _text.Get("Update.TestRisk"));
            if (_updateButton is not null)
            {
                _updateButton.Content = $"{_text.Get("Update.InstallNow")} v{selected.Version}";
                _updateButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Select version failed.", exception);
            _updateStatus!.Text = _text.Get("Update.Failed");
        }
    }

    /// <summary>弹窗展示历史版本列表，返回用户选择的版本（取消/关闭返回 null）。</summary>
    private Task<ReleaseEntry?> ShowVersionPickerAsync(List<ReleaseEntry> releases, string currentVersion)
    {
        var tcs = new TaskCompletionSource<ReleaseEntry?>();

        var window = new Window
        {
            Title = _text.Get("Update.SelectVersion"),
            Width = 420,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var hint = new TextBlock
        {
            Text = _text.Get("Update.SelectHint"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        grid.Children.Add(hint);
        Grid.SetRow(hint, 0);

        // 稳定版栏目
        var stablePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        stablePanel.Children.Add(new TextBlock
        {
            Text = _text.Get("Update.Stable"),
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        var stableListBox = new ListBox { MaxHeight = 150 };
        stablePanel.Children.Add(stableListBox);
        grid.Children.Add(stablePanel);
        Grid.SetRow(stablePanel, 1);

        // 测试版栏目
        var testPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        testPanel.Children.Add(new TextBlock
        {
            Text = _text.Get("Update.Test"),
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        testPanel.Children.Add(new TextBlock
        {
            Text = _text.Get("Update.TestWarning"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Theme.TextSecondary"]
        });
        var testListBox = new ListBox { MaxHeight = 120 };
        testPanel.Children.Add(testListBox);
        grid.Children.Add(testPanel);
        Grid.SetRow(testPanel, 2);

        foreach (var release in releases)
        {
            var target = release.IsStable ? stableListBox : testListBox;
            target.Items.Add(MakeReleaseItem(release, currentVersion));
        }

        // 两个列表选中联动：选其一则取消另一个。
        stableListBox.SelectionChanged += (_, _) =>
        {
            if (stableListBox.SelectedItem is not null)
            {
                testListBox.SelectedItem = null;
            }
        };
        testListBox.SelectionChanged += (_, _) =>
        {
            if (testListBox.SelectedItem is not null)
            {
                stableListBox.SelectedItem = null;
            }
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelButton = new Button { Content = _text.Get("Update.Cancel"), Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        var rollbackButton = new Button { Content = _text.Get("Update.SelectAction"), Width = 120 };
        cancelButton.Click += (_, _) => { tcs.TrySetResult(null); window.Close(); };
        rollbackButton.Click += (_, _) =>
        {
            var selected = (stableListBox.SelectedItem as ListBoxItem)?.Tag as ReleaseEntry
                           ?? (testListBox.SelectedItem as ListBoxItem)?.Tag as ReleaseEntry;
            tcs.TrySetResult(selected);
            window.Close();
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(rollbackButton);
        grid.Children.Add(buttons);
        Grid.SetRow(buttons, 3);

        window.Content = grid;
        window.Closed += (_, _) => tcs.TrySetResult(null);
        window.ShowDialog();

        return tcs.Task;
    }

    private ListBoxItem MakeReleaseItem(ReleaseEntry release, string currentVersion)
    {
        var isCurrent = string.Equals(release.Version, currentVersion, StringComparison.OrdinalIgnoreCase);
        var date = release.PublishedAt == DateTime.MinValue ? string.Empty : $"  ({release.PublishedAt:yyyy-MM-dd})";
        var mark = isCurrent ? $"  ← {_text.Get("Update.Current")}" : string.Empty;
        return new ListBoxItem
        {
            Content = $"v{release.Version}{date}{mark}",
            Tag = release,
            IsEnabled = !isCurrent
        };
    }

    /// <summary>下载安装包 → 退出软件并静默覆盖安装（保留配置，不删数据目录）。</summary>
    private async Task DownloadAndInstallUpdateAsync(string url, string version)
    {
        try
        {
            var tempDir = Path.GetTempPath();
            var installerPath = Path.Combine(tempDir, $"BaiYunGe_Setup_v{version}.exe");

            // 下载安装包。单独作用域：下载完立即关闭文件句柄，避免安装器启动时源文件仍被占用。
            using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                {
                    _updateStatus!.Text = _text.Get("Update.Failed");
                    return;
                }

                var total = response.Content.Headers.ContentLength ?? 0;
                // 显示进度条，让用户看到下载进度反馈。
                if (_updateProgress is not null)
                {
                    _updateProgress.Visibility = Visibility.Visible;
                }

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var file = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[1024 * 1024];
                    long received = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read));
                        received += read;
                        if (total > 0)
                        {
                            _updateStatus!.Text = _text.Format("Update.Downloading", BytesToText(received), BytesToText(total));
                            if (_updateProgress is not null)
                            {
                                _updateProgress.Value = received * 100.0 / total;
                            }
                        }
                    }

                    await file.FlushAsync();
                }
            }

            if (_updateProgress is not null)
            {
                _updateProgress.Visibility = Visibility.Collapsed;
            }

            // 界面内显示「即将退出 + UAC 提示」（不弹窗），稍候片刻后退出 + 覆盖安装。
            _updateStatus!.Text = _text.Get("Update.ReadyToInstall");
            if (_updateNotes is not null)
            {
                _updateNotes.Text = _text.Get("Update.UacHint");
                _updateNotes.Visibility = Visibility.Visible;
            }

            await Task.Delay(1500);

            // 写延迟启动脚本：等待软件完全退出后，再启动安装包静默覆盖安装。
            // 用 UTF-8 BOM 写入，避免中文用户名路径（%TEMP% 含中文）被 cmd 按 ANSI 解码乱码。
            var scriptPath = Path.Combine(tempDir, "baiyunge_update.cmd");
            await File.WriteAllTextAsync(scriptPath, BuildUpdateScript(installerPath), new System.Text.UTF8Encoding(true));

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/C \"{scriptPath}\"")
            {
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            });

            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            _logger.Error("Update failed.", exception);
            _updateStatus!.Text = _text.Get("Update.Failed");
        }
    }

    /// <summary>
    /// 构建延迟更新脚本：轮询等待 BaiYunGe.exe 完全退出（最多约 30 秒）后，
    /// 再静默启动安装包覆盖安装。不用固定 sleep——设备快慢不同，2 秒可能不够进程退出，
    /// 否则安装器检测到 AppMutex 仍被占用会静默失败。延迟用 ping 实现，兼容性最好。
    /// </summary>
    private static string BuildUpdateScript(string installerPath)
    {
        var lines = new[]
        {
            "@echo off",
            "setlocal",
            "set tries=0",
            ":waitloop",
            "tasklist /FI \"IMAGENAME eq BaiYunGe.exe\" 2>nul | find /I \"BaiYunGe.exe\" >nul",
            "if errorlevel 1 goto install",
            "if %tries% GEQ 30 goto install",
            "ping -n 2 127.0.0.1 >nul",
            "set /a tries+=1",
            "goto waitloop",
            ":install",
            $"start \"\" \"{installerPath}\" /VERYSILENT /NORESTART /SUPPRESSMSGBOXES",
        };

        return string.Join("\r\n", lines);
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 14, 0, 0),
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Theme.TextSecondary"],
            FontSize = 12
        };
    }

    private static void SelectCombo(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem box && (box.Tag as string) == tag)
            {
                combo.SelectedItem = box;
                return;
            }
        }
    }

    private static string BytesToText(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
        {
            return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
        }

        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / 1024.0 / 1024:F1} MB";
        }

        return $"{bytes / 1024.0:F0} KB";
    }

    private void NavGeneral_Click(object sender, RoutedEventArgs e) => ShowPage("general");

    private void NavShortcut_Click(object sender, RoutedEventArgs e) => ShowPage("shortcut");

    private void NavDictionary_Click(object sender, RoutedEventArgs e) => ShowPage("dictionary");

    private void NavModel_Click(object sender, RoutedEventArgs e) => ShowPage("model");

    private void NavCalibration_Click(object sender, RoutedEventArgs e) => ShowPage("calibration");

    private void NavAbout_Click(object sender, RoutedEventArgs e)
    {
        ShowPage("about");
        // 进入关于界面后，红点消失（每个新版本只提示一次）。
        HideUpdateBadge();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 关闭 = 隐藏到托盘，不退出。
        base.OnClosing(e);
        Hide();
        e.Cancel = true;
    }
}
