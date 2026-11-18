using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CONVERTit.App.Localization;
using CONVERTit.App.ViewModels;
using CONVERTit.Core.Machine;

namespace CONVERTit.App.Services;

/// <summary>
/// Icon in the Windows notification area: left click opens the window, the menu offers queue control,
/// the performance profile and Exit. The tooltip shows the queue progress; a dot marks running jobs.
/// Visible while the window is hidden or when a tray option is on.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly Action _show;
    private readonly Action _exit;
    private readonly TrayIcon _icon;
    private readonly WindowIcon? _idleIcon;
    private readonly WindowIcon? _activeIcon;
    private bool _windowHidden;
    private bool _active;
    private string _menuLanguage = "";
    private PerformanceProfile? _menuProfile;
    private bool? _menuPaused;
    private bool? _menuBusy;

    public TrayService(MainViewModel vm, Action show, Action exit)
    {
        _vm = vm;
        _show = show;
        _exit = exit;
        _idleIcon = Load("tray.png");
        _activeIcon = Load("tray-active.png");
        _icon = new TrayIcon { Icon = _idleIcon, ToolTipText = "CONVERTit", IsVisible = false };
        _icon.Clicked += (_, _) => _show();
        if (Application.Current is { } app)
        {
            TrayIcon.SetIcons(app, new TrayIcons { _icon });
        }

        vm.TrayStateChanged += Update;
        Update();
    }

    /// <summary>The window was hidden or shown (the icon is always visible while the window is hidden).</summary>
    public bool WindowHidden
    {
        get => _windowHidden;
        set
        {
            _windowHidden = value;
            Update();
        }
    }

    public void Update()
    {
        var s = _vm.Settings;
        _icon.IsVisible = _windowHidden || s.MinimizeToTray || s.CloseToTray;

        var busy = _vm.Queue.IsBusy;
        if (busy != _active)
        {
            _active = busy;
            _icon.Icon = busy ? _activeIcon ?? _idleIcon : _idleIcon;
        }

        var tip = busy ? $"CONVERTit · {_vm.HeaderStatusText}" : "CONVERTit";
        _icon.ToolTipText = tip.Length > 120 ? tip[..120] : tip;

        // Rebuild the menu only when something it shows changed (language, profile, pause state, busy).
        if (_menuLanguage != Loc.Language || _menuProfile != s.PerformanceProfile || _menuPaused != _vm.Queue.IsPaused || _menuBusy != busy)
        {
            _menuLanguage = Loc.Language;
            _menuProfile = s.PerformanceProfile;
            _menuPaused = _vm.Queue.IsPaused;
            _menuBusy = busy;
            _icon.Menu = BuildMenu(busy);
        }
    }

    private NativeMenu BuildMenu(bool busy)
    {
        var menu = new NativeMenu();
        menu.Add(Item(Loc.T("TrayOpen"), _show));
        menu.Add(Item(Loc.T("NavQueue"), () =>
        {
            _vm.NavigateTo(_vm.QueuePage);
            _show();
        }));
        menu.Add(new NativeMenuItemSeparator());

        var pause = Item(Loc.T(_vm.Queue.IsPaused ? "TrayResume" : "TrayPause"), () => _vm.TogglePauseCommand.Execute(null));
        pause.IsEnabled = busy || _vm.Queue.IsPaused;
        menu.Add(pause);
        var cancel = Item(Loc.T("TrayCancelAll"), () => _vm.CancelAllJobsCommand.Execute(null));
        cancel.IsEnabled = busy;
        menu.Add(cancel);
        menu.Add(new NativeMenuItemSeparator());

        var profiles = new NativeMenu();
        foreach (var profile in new[] { PerformanceProfile.Auto, PerformanceProfile.Eco, PerformanceProfile.Balanced, PerformanceProfile.Maximum })
        {
            var item = Item(Loc.Enum(profile), () => _vm.SetPerformanceProfile(profile));
            item.ToggleType = NativeMenuItemToggleType.Radio;
            item.IsChecked = _vm.Settings.PerformanceProfile == profile;
            profiles.Add(item);
        }

        menu.Add(new NativeMenuItem(Loc.T("LblPerformanceProfile")) { Menu = profiles });
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(Loc.T("TrayExit"), _exit));
        return menu;
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private static WindowIcon? Load(string asset)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://CONVERTit/Assets/{asset}"));
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _vm.TrayStateChanged -= Update;
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
