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

    private string _currentPage = "general";

    // 页面控件引用
    private ComboBox? _languageCombo;
    private ComboBox? _themeCombo;
    private ComboBox? _micCombo;
    private ComboBox? _deviceCombo;
    private ComboBox? _recognitionLangCombo;
    private TextBox? _shortcutBox;
    private ComboBox? _modeCombo;
    private CheckBox? _dictEnabled;
    private ListView? _dictList;
    private TextBox? _aliasBox;
    private TextBox? _targetBox;
    private TextBox? _modelDirBox;
    private ProgressBar? _modelProgress;
    private TextBlock? _modelStatus;

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

        RefreshLocalization();
        ShowPage("general");
    }

    public event Action? SettingsChanged;

    public event Action? ModelInstalled;

    private void RefreshLocalization()
    {
        Title = _text.Get("Settings.Title");
        NavGeneral.Content = _text.Get("Page.General");
        NavShortcut.Content = _text.Get("Page.Shortcut");
        NavDictionary.Content = _text.Get("Page.Dictionary");
        NavModel.Content = _text.Get("Page.Model");
        ShowPage(_currentPage);
    }

    private void ShowPage(string page)
    {
        _currentPage = page;
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
        }
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

        return panel;
    }

    private UIElement BuildModelPage()
    {
        var panel = new StackPanel();

        panel.Children.Add(Label(_text.Get("Model.Directory")));
        var dirGrid = new Grid();
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _modelDirBox = new TextBox { Text = _settings.ModelDirectory, Margin = new Thickness(0, 4, 4, 0) };
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
            return;
        }

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
                    _modelStatus.Text = _text.Format("Model.Progress", p.FileName, BytesToText(p.ReceivedBytes), BytesToText(p.TotalBytes));
                }
            });

            var summary = await _downloader.DownloadAsync(
                directory,
                beforeFileReplaceAsync: null,
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

    protected override void OnClosing(CancelEventArgs e)
    {
        // 关闭 = 隐藏到托盘，不退出。
        base.OnClosing(e);
        Hide();
        e.Cancel = true;
    }
}
