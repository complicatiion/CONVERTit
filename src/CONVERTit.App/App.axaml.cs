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
    private bool _ready;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
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

            _mainWindow = new MainWindow { DataContext = _viewModel };
            host.MainWindow = _mainWindow;
            desktop.MainWindow = _mainWindow;

            desktop.ShutdownRequested += (_, _) => _viewModel.SaveState();
            desktop.Exit += (_, _) =>
            {
                _viewModel.SaveState();
                _viewModel.Dispose();
            };

            if (OperatingSystem.IsWindows())
            {
                Platform.Windows.Notifications.Register("CONVERTit", AppEnvironment.NotificationIconPath, AppEnvironment.ExecutablePath);
                ExplorerIntegration.Refresh(settings);
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

        var any = false;
        while (Program.Requests.TryDequeue(out var request))
        {
            any = true;
            _viewModel.QueueExternal(request.Action, request.Files);
        }

        if (any)
        {
            ShowMainWindow();
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
        if (_mainWindow is null)
        {
            return;
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
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
