using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.App.ViewModels;
using CONVERTit.App.Views;

namespace CONVERTit.App;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private TrayService? _tray;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _ready;
    private bool _windowHidden;
    private bool _exiting;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Middle-click scrolling (browser style) in every ScrollViewer.
        CONVERTit.App.Controls.AutoScroll.Install();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettings.Load();
            if (settings.IsNew)
            {
                // First start: German for German systems (UI language or keyboard layout), English for everyone else.
                settings.Language = DetectLanguage();
                settings.Save();
            }

            Loc.Apply(settings.Language ?? Loc.DefaultLanguage);

            var host = new UiHost();
            _viewModel = new MainViewModel(settings, host);
            _viewModel.ApplyTheme(settings.Theme);
            _viewModel.ShowWindowRequested += ShowMainWindow;
            _viewModel.HideToTrayRequested += HideToTray;
            _viewModel.ExitRequested += Exit;
            _viewModel.IsWindowHidden = () => _windowHidden;

            _desktop = desktop;
            // The app ends explicitly (window closed without tray, tray menu Exit, background jobs done).
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _mainWindow = new MainWindow { DataContext = _viewModel };
            host.MainWindow = _mainWindow;
            _mainWindow.Closed += (_, _) => Shutdown();
            _mainWindow.Opened += (_, _) => _viewModel.OnWindowShown();
            _tray = new TrayService(_viewModel, ShowMainWindow, Exit);

            // Start hidden: started with Windows (Settings › Start in notification area) or only for an Explorer
            // background job ("Convert to MP3" …). Everything else opens the window as usual.
            var hidden = (Program.StartedFromAutostart && settings.StartInTray)
                || (settings.BackgroundExplorerJobs && MainViewModel.IsBackgroundAction(Program.StartAction));
            if (hidden)
            {
                _windowHidden = true;
                _tray.WindowHidden = true;
                _viewModel.ExitWhenDrained = !Program.StartedFromAutostart && settings.ExitAfterBackgroundJobs;
            }
            else
            {
                // The lifetime shows the main window when it starts.
                desktop.MainWindow = _mainWindow;
            }

            desktop.ShutdownRequested += (_, _) => _viewModel.SaveState();
            desktop.Exit += (_, _) =>
            {
                _tray?.Dispose();
                _viewModel.SaveState();
                _viewModel.Dispose();
            };

            if (OperatingSystem.IsWindows())
            {
                Platform.Windows.Notifications.Register("CONVERTit", AppEnvironment.NotificationIconPath, AppEnvironment.ExecutablePath);
                ExplorerIntegration.Refresh(settings);
                // File associations follow the program path (after updates or a moved portable folder).
                if (settings.Associations != AssociationMode.Off)
                {
                    try
                    {
                        FileAssociationService.Apply(settings);
                    }
                    catch
                    {
                        // Registry not writable: keep what is there.
                    }
                }
            }

            StartShowSignalListener();
            Program.RequestArrived += () => Dispatcher.UIThread.Post(DrainRequests);

            // FFmpeg lookup and encoder test run in the background after the window is visible.
            Dispatcher.UIThread.Post(async () =>
            {
                await _viewModel.RefreshFfmpegAsync();
                _ready = true;
                DrainRequests();
                _viewModel.OfferFfmpegSetup();
            }, DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Files and actions from Explorer, "Open with" or later starts.</summary>
    private void DrainRequests()
    {
        if (!_ready || _viewModel is null)
        {
            return;
        }

        while (Program.Requests.TryDequeue(out var request))
        {
            if (request.Files.Length == 0)
            {
                // A second start without files: bring the window back.
                ShowMainWindow();
                continue;
            }

            // The view model decides whether the window is needed (background actions stay in the tray).
            _viewModel.QueueExternal(request.Action, request.Files);
        }
    }

    private static string DetectLanguage()
    {
        try
        {
            if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de")
            {
                return "de";
            }

            if (OperatingSystem.IsWindows() && Platform.Windows.SystemLanguage.KeyboardPrimaryLanguage() == 0x07)
            {
                return "de";
            }
        }
        catch
        {
            // Detection is a convenience only.
        }

        return Loc.DefaultLanguage;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null || _exiting)
        {
            return;
        }

        if (_desktop is not null && _desktop.MainWindow is null)
        {
            _desktop.MainWindow = _mainWindow;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.RestoreFromMinimized();
        }

        _mainWindow.Activate();
        _windowHidden = false;
        if (_tray is not null)
        {
            _tray.WindowHidden = false;
        }

        _viewModel?.OnWindowShown();
    }

    /// <summary>Window → notification area (close/minimize to tray, "continue in background").</summary>
    private void HideToTray()
    {
        if (_mainWindow is null || _viewModel is null)
        {
            return;
        }

        _windowHidden = true;
        if (_tray is not null)
        {
            _tray.WindowHidden = true;
        }

        _mainWindow.Hide();
        if (!_viewModel.Settings.TrayHintShown)
        {
            // Once: say where CONVERTit went.
            _viewModel.Settings.TrayHintShown = true;
            _viewModel.Settings.Save();
            _viewModel.ShowSystemNotification("CONVERTit", Loc.T("NotifyTrayHint"), null);
        }
    }

    /// <summary>Tray menu "Exit" or background jobs done: running jobs are confirmed in the window first.</summary>
    private void Exit()
    {
        if (_mainWindow is null || _viewModel is null)
        {
            Shutdown();
            return;
        }

        if (_viewModel.Queue.IsBusy)
        {
            // Running jobs: the window asks first (exit anyway or cancel).
            ShowMainWindow();
            _mainWindow.CloseForExit();
            return;
        }

        Shutdown();
    }

    private void Shutdown()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        if (_mainWindow is not null)
        {
            _mainWindow.ExitAllowed = true;
        }

        _desktop?.Shutdown();
    }

    /// <summary>A second app start without a reachable pipe signals "show window".</summary>
    private void StartShowSignalListener()
    {
        var signal = Program.ShowSignal;
        if (signal is null)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                while (signal.WaitOne())
                {
                    Dispatcher.UIThread.Post(ShowMainWindow);
                }
            }
            catch (ObjectDisposedException)
            {
                // App is exiting.
            }
        })
        {
            IsBackground = true,
            Name = "CONVERTit.ShowSignal"
        };
        thread.Start();
    }
}
