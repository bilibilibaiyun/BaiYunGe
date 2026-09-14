using System.Windows;
using System.Windows.Controls;
using WixToolset.BootstrapperApplicationApi;

namespace BaiYunGe.Bootstrapper;

public partial class MainWindow : Window
{
    private readonly InstallerBootstrapper _bootstrapper;
    private readonly InstallerText _text;
    private bool _detected;
    private bool _busy;

    public MainWindow(
        InstallerBootstrapper bootstrapper,
        InstallerText text)
    {
        _bootstrapper = bootstrapper;
        _text = text;
        InitializeComponent();

        LanguageCombo.Items.Add(new ComboBoxItem
        {
            Content = _text.Get("Chinese"),
            Tag = "zh-CN"
        });
        LanguageCombo.Items.Add(new ComboBoxItem
        {
            Content = _text.Get("English"),
            Tag = "en-US"
        });
        SelectLanguage(_text.CurrentLanguage);
        ApplyLanguage();
        StatusText.Text = _text.Get("Detecting");

        Loaded += (_, _) =>
        {
            _bootstrapper.Engine.CloseSplashScreen();
            _bootstrapper.Detect();
        };
    }

    internal void ShowDetected()
    {
        _detected = true;
        SetBusy(false);
        StatusText.Text = string.Empty;
        ProgressBar.Visibility = Visibility.Collapsed;

        if (_bootstrapper.IsInstalled)
        {
            LanguageCombo.IsEnabled = false;
            if (_bootstrapper.IsUpgradeAvailable)
            {
                InstallButton.Visibility = Visibility.Visible;
                RepairButton.Visibility = Visibility.Collapsed;
                UninstallButton.Visibility = Visibility.Visible;
            }
            else
            {
                InstallButton.Visibility = Visibility.Collapsed;
                RepairButton.Visibility = Visibility.Visible;
                UninstallButton.Visibility = Visibility.Visible;
            }
        }
        else
        {
            LanguageCombo.IsEnabled = true;
            InstallButton.Visibility = Visibility.Visible;
            RepairButton.Visibility = Visibility.Collapsed;
            UninstallButton.Visibility = Visibility.Collapsed;
        }

        ApplyLanguage();
    }

    internal void SetBusy(bool busy)
    {
        _busy = busy;
        LanguageCombo.IsEnabled = !busy && !_bootstrapper.IsInstalled;
        InstallButton.IsEnabled = !busy;
        RepairButton.IsEnabled = !busy;
        UninstallButton.IsEnabled = !busy;
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SetBusy(string message)
    {
        StatusText.Text = message;
        SetBusy(true);
    }

    internal void SetProgress(int percentage)
    {
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Minimum = 0;
        ProgressBar.Maximum = 100;
        ProgressBar.Value = Math.Clamp(percentage, 0, 100);
    }

    internal void ShowComplete(bool restartRequired)
    {
        SetBusy(false);
        StatusText.Text = string.Empty;
        MessageText.Text = restartRequired
            ? _text.Get("RestartRequired")
            : _text.Get("Complete");
        InstallButton.Visibility = Visibility.Collapsed;
        RepairButton.Visibility = Visibility.Collapsed;
        UninstallButton.Visibility = Visibility.Collapsed;
        CancelButton.Content = _text.Get("Close");
    }

    internal void ShowFailure()
    {
        SetBusy(false);
        StatusText.Text = string.Empty;
        MessageText.Text = _text.Get("Failed");
        InstallButton.Visibility = Visibility.Collapsed;
        RepairButton.Visibility = Visibility.Collapsed;
        UninstallButton.Visibility = Visibility.Collapsed;
        CancelButton.Content = _text.Get("Close");
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string language)
        {
            _bootstrapper.SelectedLanguage = language;
            _text.ChangeLanguage(language);
            ApplyLanguage();
        }
    }

    private void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        StartAction(LaunchAction.Install);
    }

    private void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        StartAction(LaunchAction.Repair);
    }

    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        StartAction(LaunchAction.Uninstall);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _bootstrapper.Cancel();
            return;
        }

        _bootstrapper.CloseCompleted(_bootstrapper.IsInstalled ? 0 : 1223);
    }

    private void StartAction(LaunchAction action)
    {
        _bootstrapper.Plan(action);
    }

    private void SelectLanguage(string language)
    {
        foreach (var rawItem in LanguageCombo.Items)
        {
            if (rawItem is ComboBoxItem item &&
                string.Equals(item.Tag as string, language, StringComparison.Ordinal))
            {
                LanguageCombo.SelectedItem = item;
                return;
            }
        }

        LanguageCombo.SelectedIndex = 0;
    }

    private void ApplyLanguage()
    {
        Title = _text.Get("Title");
        TitleText.Text = _text.Get("AppName");
        LanguageLabel.Text = _text.Get("Language");
        InstallButton.Content = _bootstrapper.IsUpgradeAvailable
            ? _text.Get("Update")
            : _text.Get("Install");
        RepairButton.Content = _text.Get("Repair");
        UninstallButton.Content = _text.Get("Uninstall");
        CancelButton.Content = _text.Get("Cancel");

        if (_detected)
        {
            MessageText.Text = _bootstrapper.IsUpgradeAvailable
                ? _text.Get("ReadyUpdate")
                : _bootstrapper.IsInstalled
                    ? _text.Get("ReadyModify")
                    : _text.Get("ReadyInstall");
        }
    }
}
