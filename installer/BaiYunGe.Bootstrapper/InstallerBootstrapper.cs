using System.Windows;
using WixToolset.BootstrapperApplicationApi;

namespace BaiYunGe.Bootstrapper;

public sealed class InstallerBootstrapper : BootstrapperApplication
{
    private const string ProductCode = "{99346DC1-3B34-4740-958D-C4378D00A039}";

    private const string Chinese = "zh-CN";

    private const string English = "en-US";

    private IEngine? _engine;
    private IBootstrapperCommand? _command;
    private InstallerText? _text;
    private MainWindow? _window;
    private Window? _hostWindow;
    private string _selectedLanguage = InstallerText.FromOperatingSystem();
    private string _installedLanguage = string.Empty;
    private bool _relatedUpgradeAvailable;
    private bool _applyRequested;
    private bool _cancelRequested;
    private bool _isSilent;
    private int _exitCode;

    public InstallerBootstrapper()
    {
        Create += OnCreate;
        DetectPackageComplete += OnDetectPackageComplete;
        DetectRelatedMsiPackage += OnDetectRelatedMsiPackage;
        DetectComplete += OnDetectComplete;
        PlanComplete += OnPlanComplete;
        Progress += OnProgress;
        ApplyComplete += OnApplyComplete;
        Error += OnError;
    }

    protected override void Run()
    {
        if (_engine is null || _command is null)
        {
            _engine?.Quit(1);
            return;
        }

        _text = new InstallerText(_selectedLanguage);
        Engine.SetVariableString("InstallLanguage", _selectedLanguage, false);
        _isSilent = _command.Display == Display.None;
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };

        if (_isSilent)
        {
            _hostWindow = new Window
            {
                Width = 0,
                Height = 0,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize
            };
            _hostWindow.Loaded += (_, _) => Engine.Detect();
            app.Run(_hostWindow);
            Engine.Quit(_exitCode);
            return;
        }

        _window = new MainWindow(
            this,
            _text);
        app.Run(_window);
        _engine.Quit(_exitCode);
    }

    internal IEngine Engine => _engine ?? throw new InvalidOperationException("The Burn engine is not initialized.");

    internal string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            _selectedLanguage = InstallerText.NormalizeLanguage(value);
            Engine.SetVariableString("InstallLanguage", _selectedLanguage, false);
        }
    }

    internal string InstalledLanguage => _installedLanguage;

    internal bool IsInstalled => !string.IsNullOrEmpty(_installedLanguage);

    internal bool IsUpgradeAvailable => _relatedUpgradeAvailable;

    internal void Detect()
    {
        Engine.Detect();
    }

    internal void Plan(LaunchAction action)
    {
        _applyRequested = true;
        _cancelRequested = false;
        _window?.SetBusy(GetActionMessage(action));
        Engine.Plan(action);
    }

    internal void Cancel()
    {
        _cancelRequested = true;
        _exitCode = 1223;
        _window?.Close();
    }

    internal void CloseCompleted(int exitCode)
    {
        _exitCode = exitCode;
        _window?.Close();
    }

    private void OnCreate(object? sender, CreateEventArgs args)
    {
        _engine = args.Engine;
        _command = args.Command;
    }

    private void OnDetectPackageComplete(object? sender, DetectPackageCompleteEventArgs args)
    {
        if (args.State != PackageState.Present)
        {
            return;
        }

        _installedLanguage = args.PackageId switch
        {
            "BaiYunGe_zhCN" => Chinese,
            "BaiYunGe_enUS" => English,
            _ => _installedLanguage
        };
        _relatedUpgradeAvailable = false;
    }

    private void OnDetectRelatedMsiPackage(object? sender, DetectRelatedMsiPackageEventArgs args)
    {
        if (!string.Equals(args.ProductCode, ProductCode, StringComparison.OrdinalIgnoreCase) ||
            FindInstalledProductLanguage() is not { } installedLanguage ||
            !Version.TryParse(args.Version, out var installedVersion))
        {
            return;
        }

        _installedLanguage = installedLanguage;
        _relatedUpgradeAvailable = installedVersion < new Version("1.0.2.0");
    }

    private void OnDetectComplete(object? sender, DetectCompleteEventArgs args)
    {
        Dispatch(() =>
        {
            if (args.Status < 0)
            {
                if (_isSilent)
                {
                    CompleteSilent(args.Status);
                }
                else
                {
                    _exitCode = args.Status;
                    _window?.ShowFailure();
                }

                return;
            }

            if (IsInstalled)
            {
                SelectedLanguage = _installedLanguage;
                _text?.ChangeLanguage(_installedLanguage);
            }

            if (_isSilent)
            {
                Plan(GetCommandAction());
            }
            else
            {
                _window?.ShowDetected();
            }
        });
    }

    private void OnPlanComplete(object? sender, PlanCompleteEventArgs args)
    {
        Dispatch(() =>
        {
            if (args.Status < 0)
            {
                if (_isSilent)
                {
                    CompleteSilent(args.Status);
                }
                else
                {
                    _exitCode = args.Status;
                    _window?.ShowFailure();
                }

                return;
            }

            if (_applyRequested)
            {
                var handle = _isSilent
                    ? new System.Windows.Interop.WindowInteropHelper(_hostWindow!).Handle
                    : new System.Windows.Interop.WindowInteropHelper(_window!).Handle;
                Engine.Apply(handle);
            }
        });
    }

    private void OnProgress(object? sender, ProgressEventArgs args)
    {
        args.Cancel = _cancelRequested;
        Dispatch(() => _window?.SetProgress(args.OverallPercentage));
    }

    private void OnApplyComplete(object? sender, ApplyCompleteEventArgs args)
    {
        Dispatch(() =>
        {
            if (args.Status >= 0)
            {
                if (_isSilent)
                {
                    CompleteSilent(0);
                }
                else
                {
                    _window?.ShowComplete(args.Restart == ApplyRestart.RestartRequired);
                }

                return;
            }

            if (_isSilent)
            {
                CompleteSilent(args.Status);
            }
            else
            {
                _exitCode = args.Status;
                _window?.ShowFailure();
            }
        });
    }

    private void OnError(object? sender, ErrorEventArgs args)
    {
        Dispatch(() =>
        {
            var exitCode = Math.Min(_exitCode == 0 ? args.ErrorCode : _exitCode, -1);
            if (_isSilent)
            {
                CompleteSilent(exitCode);
            }
            else
            {
                _exitCode = exitCode;
                _window?.ShowFailure();
            }
        });
    }

    private string GetActionMessage(LaunchAction action)
    {
        return action switch
        {
            LaunchAction.Install => _text!.Get("Installing"),
            LaunchAction.Repair => _text!.Get("Repairing"),
            LaunchAction.Uninstall => _text!.Get("Uninstalling"),
            _ => _text!.Get("Preparing")
        };
    }

    private LaunchAction GetCommandAction()
    {
        return _command?.Action switch
        {
            LaunchAction.Uninstall => LaunchAction.Uninstall,
            LaunchAction.Repair => LaunchAction.Repair,
            LaunchAction.Layout => LaunchAction.Layout,
            _ => _relatedUpgradeAvailable
                ? LaunchAction.Install
                : IsInstalled ? LaunchAction.Modify : LaunchAction.Install
        };
    }

    private static string? FindInstalledProductLanguage()
    {
        var uninstallPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var uninstallPath in uninstallPaths)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                System.IO.Path.Combine(uninstallPath, ProductCode));
            var displayName = key?.GetValue("DisplayName") as string;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            return displayName.Contains("白云歌", StringComparison.Ordinal)
                ? Chinese
                : English;
        }

        return null;
    }

    private void CompleteSilent(int exitCode)
    {
        _exitCode = exitCode;
        _cancelRequested = true;
        _hostWindow?.Dispatcher.BeginInvoke(() => _hostWindow.Close());
    }

    private static void Dispatch(Action action)
    {
        if (Application.Current is null)
        {
            ThreadPool.QueueUserWorkItem(_ => action());
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
