using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CONVERTit.App.Controls;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.App.ViewModels;
using CONVERTit.Platform.Windows;

namespace CONVERTit.App.Views;

public partial class MainWindow : Window
{
    private TaskbarProgress? _taskbar;
    private bool _confirmedClose;
    private bool _exitRequested;
    private bool _sized;
    private WindowState _lastState = WindowState.Normal;

    /// <summary>Set by the app right before it shuts down: closing is never cancelled then.</summary>
    public bool ExitAllowed { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this);
        Closing += OnClosing;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            vm.IsWindowActive = () => IsActive && WindowState != WindowState.Minimized;
            vm.PropertyChanged += (_, e) =>
            {
                // ShowToast sets Toast before HasToast: also react to HasToast so the first toast animates too.
                if (e.PropertyName is nameof(MainViewModel.Toast) or nameof(MainViewModel.HasToast) && vm.HasToast)
                {
                    Motion.FadeIn(ToastHost, offsetX: 24, offsetY: 0, milliseconds: 220);
                }

                if (e.PropertyName == nameof(MainViewModel.ShowFfmpegSetup) && vm.ShowFfmpegSetup)
                {
                    Motion.FadeIn(SetupOverlay, offsetY: 0, milliseconds: 180);
                    Motion.FadeIn(SetupCard, offsetY: 18, milliseconds: 260);
                }
            };
            vm.QueueProgressChanged += OnQueueProgress;
            vm.DisplayProfileChanged += ApplyDisplayProfile;
            ApplyDisplayProfile();
        };

        Opened += (_, _) =>
        {
            // Opened also fires when the window comes back from the notification area: size it only once.
            if (_sized)
            {
                return;
            }

            _sized = true;
            ApplyDisplayProfile();
            FitToScreen();
            // Maximized by default (Settings › Appearance › Start maximized).
            if (ViewModel?.Settings.StartMaximized != false)
            {
                WindowState = WindowState.Maximized;
            }
        };
        ScalingChanged += (_, _) => ApplyDisplayProfile();

        // Minimize to the notification area (Settings › Background); remember the state for restoring.
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty)
            {
                return;
            }

            if (WindowState == WindowState.Minimized)
            {
                if (ViewModel is { Settings.MinimizeToTray: true } vm)
                {
                    vm.HideToTray();
                }
            }
            else
            {
                _lastState = WindowState;
            }
        };

        // Mouse back/forward buttons and a click outside the FFmpeg pill (folds it again).
        AddHandler(PointerPressedEvent, OnPointerPressedTunnel, RoutingStrategies.Tunnel);
        KeyDown += OnKeyDown;

        // Files and folders can be dropped anywhere on the window.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>Restores the state from before minimizing (maximized stays maximized).</summary>
    public void RestoreFromMinimized() => WindowState = _lastState == WindowState.Minimized ? WindowState.Normal : _lastState;

    /// <summary>Tray menu "Exit": bypasses close-to-tray; running jobs are still confirmed.</summary>
    public void CloseForExit()
    {
        _exitRequested = true;
        Close();
    }

    // ---------------------------------------------------------------- History navigation & pill

    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var point = e.GetCurrentPoint(this).Properties;
        if (point.IsXButton1Pressed)
        {
            vm.GoBackCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (point.IsXButton2Pressed)
        {
            vm.GoForwardCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.FfmpegPillExpanded && e.Source is Visual source && !ReferenceEquals(source, FfmpegPillButton)
            && !FfmpegPillButton.IsVisualAncestorOf(source))
        {
            vm.CollapseFfmpegPill();
        }
    }

    /// <summary>
    /// App-wide shortcuts (list in docs\SHORTCUTS.txt). Pages handle their own keys first (Trim &amp; Loop: Space, I/O,
    /// S/C, Del, arrows); only keys nobody handled arrive here. Plain keys are ignored while typing in a text box.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var typing = e.Source is TextBox;
        var mods = e.KeyModifiers;
        var page = vm.CurrentPage;
        var sources = (page as ConvertPageViewModel)?.Sources;
        var handled = true;
        switch (e.Key)
        {
            case Key.Left when mods == KeyModifiers.Alt:
                vm.GoBackCommand.Execute(null);
                break;
            case Key.Right when mods == KeyModifiers.Alt:
                vm.GoForwardCommand.Execute(null);
                break;
            case Key.Up when mods == KeyModifiers.Control:
                vm.NavigateRelative(-1);
                break;
            case Key.Down when mods == KeyModifiers.Control:
                vm.NavigateRelative(1);
                break;
            case Key.D1 when mods == KeyModifiers.Control:
                vm.NavigateTo(vm.QuickConvert);
                break;
            case Key.D2 when mods == KeyModifiers.Control:
                vm.NavigateTo(vm.Compress);
                break;
            case Key.D3 when mods == KeyModifiers.Control:
                vm.NavigateTo(vm.QueuePage);
                break;
            case Key.D when mods == KeyModifiers.Control:
                vm.NavigateTo(vm.Dependencies);
                break;
            case Key.OemComma when mods == KeyModifiers.Control:
                vm.NavigateTo(vm.SettingsPage);
                break;
            case Key.B when mods == KeyModifiers.Control:
                vm.ToggleSidebarCommand.Execute(null);
                break;
            case Key.F5 when mods == KeyModifiers.None:
                vm.Dependencies.CheckAllCommand.Execute(null);
                break;
            case Key.O when mods == KeyModifiers.Control && sources is not null:
                sources.AddFilesCommand.Execute(null);
                break;
            case Key.O when mods == (KeyModifiers.Control | KeyModifiers.Shift) && sources is not null:
                sources.AddFolderCommand.Execute(null);
                break;
            case Key.A when mods == KeyModifiers.Control && !typing && sources is not null:
                sources.SelectAllCommand.Execute(null);
                break;
            case Key.Enter when mods == KeyModifiers.Control && page is ConvertPageViewModel convert:
                // Selected files only when there is a selection, otherwise all (like the footer buttons).
                convert.QueueSelection();
                break;
            case Key.Enter when mods == (KeyModifiers.Control | KeyModifiers.Shift) && page is ConvertPageViewModel { ShowConvertNow: true } now:
                if (now.ConvertNowCommand.CanExecute(null))
                {
                    now.ConvertNowCommand.Execute(null);
                }

                break;
            case Key.Delete when mods == KeyModifiers.None && !typing && sources is not null:
                sources.RemoveSelectedCommand.Execute(null);
                break;
            case Key.Space when mods == KeyModifiers.None && !typing && sources is { ShowMiniPlayer: true, SelectedItem: { } item }:
                sources.TogglePlayCommand.Execute(item);
                break;
            case Key.Escape:
                if (vm.ShowFfmpegSetup)
                {
                    vm.CloseFfmpegSetupCommand.Execute(null);
                }
                else if (vm.FfmpegPillExpanded)
                {
                    vm.CollapseFfmpegPill();
                }
                else if (typing)
                {
                    Focus();
                }
                else
                {
                    handled = false;
                }

                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }

    // ---------------------------------------------------------------- Sidebar groups

    private void OnGroupClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NavGroup group } button || ViewModel is not { } vm)
        {
            return;
        }

        if (!vm.IsSidebarCollapsed)
        {
            vm.ToggleGroup(group);
            if (group.IsExpanded && button.Parent is StackPanel panel && panel.Children.Count > 1)
            {
                // Sub entries slide in softly.
                Motion.FadeIn(panel.Children[1], offsetY: -6, milliseconds: 200);
            }

            return;
        }

        // Collapsed sidebar: the sub pages unfold below the group as icons (accordion or independent, see Settings).
        vm.ToggleRailGroup(group);
        if (group.IsRailExpanded && button.Parent is StackPanel railPanel && railPanel.Children.Count > 1)
        {
            Motion.FadeIn(railPanel.Children[1], offsetY: -6, milliseconds: 200);
        }
    }

    // ---------------------------------------------------------------- Display profiles

    /// <summary>Scale per profile; Auto follows the screen size (in device independent pixels).</summary>
    private void ApplyDisplayProfile()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var scale = vm.Settings.DisplayProfile switch
        {
            DisplayProfile.ExtraSmall => 0.78,
            DisplayProfile.Small => 0.9,
            DisplayProfile.Medium => 1.0,
            DisplayProfile.Large => 1.15,
            DisplayProfile.ExtraLarge => 1.3,
            _ => AutoScale()
        };

        ScaleRoot.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? null : new ScaleTransform(scale, scale);
        MinWidth = Math.Round(1000 * scale);
        MinHeight = Math.Round(640 * scale);
    }

    private double AutoScale()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return 1;
        }

        var width = screen.WorkingArea.Width / screen.Scaling;
        var height = screen.WorkingArea.Height / screen.Scaling;
        if (width < 1150 || height < 700)
        {
            return 0.8; // tablets and small notebooks
        }

        if (width < 1400 || height < 820)
        {
            return 0.9;
        }

        if (width >= 3200)
        {
            return 1.3; // large monitors at 100 % Windows scaling
        }

        return width >= 2400 ? 1.15 : 1;
    }

    /// <summary>The start size must fit on small screens (the window keeps its minimum size).</summary>
    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null || WindowState != WindowState.Normal)
        {
            return;
        }

        var maxWidth = screen.WorkingArea.Width / screen.Scaling * 0.94;
        var maxHeight = screen.WorkingArea.Height / screen.Scaling * 0.92;
        if (Width > maxWidth || Height > maxHeight)
        {
            Width = Math.Max(MinWidth, Math.Min(Width, maxWidth));
            Height = Math.Max(MinHeight, Math.Min(Height, maxHeight));
            Position = new PixelPoint(
                screen.WorkingArea.X + (int)((screen.WorkingArea.Width - Width * screen.Scaling) / 2),
                screen.WorkingArea.Y + (int)((screen.WorkingArea.Height - Height * screen.Scaling) / 2));
        }
    }

    // ---------------------------------------------------------------- Drag & drop

    private static List<string> DroppedPaths(DragEventArgs e)
    {
        var paths = new List<string>();
        foreach (var item in e.DataTransfer.TryGetFiles() ?? [])
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private static bool HasFiles(DragEventArgs e) => e.DataTransfer.Formats.Contains(DataFormat.File);

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (HasFiles(e))
        {
            DropOverlay.IsVisible = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => DropOverlay.IsVisible = false;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        DropOverlay.IsVisible = false;
        ViewModel?.Drop(DroppedPaths(e));
    }

    // ---------------------------------------------------------------- Taskbar

    private void OnQueueProgress(bool busy, double fraction, bool failed)
    {
        if (!OperatingSystem.IsWindows() || ViewModel is not { } vm)
        {
            return;
        }

        _taskbar ??= new TaskbarProgress();
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (!vm.Settings.TaskbarProgress || !busy)
        {
            _taskbar.Set(hwnd, TaskbarState.None);
            return;
        }

        _taskbar.Set(hwnd, failed ? TaskbarState.Error : TaskbarState.Normal, Math.Max(0.02, fraction));
    }

    // ---------------------------------------------------------------- Closing

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ExitAllowed || _confirmedClose || ViewModel is not { } vm
            || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown)
        {
            return;
        }

        // Settings › Background: closing keeps CONVERTit in the notification area (not for tray menu "Exit").
        if (!_exitRequested && vm.Settings.CloseToTray)
        {
            e.Cancel = true;
            vm.HideToTray();
            return;
        }

        if (!vm.Queue.IsBusy)
        {
            return;
        }

        e.Cancel = true;
        if (_exitRequested)
        {
            // Tray menu "Exit" with running jobs: exit anyway or keep going.
            _exitRequested = false;
            var exit = new ConfirmWindow(Loc.T("ConfirmExitTitle"), Loc.T("ConfirmExitText"), Loc.T("BtnExitAnyway"), danger: true, confirmIcon: "IconClose");
            if (await exit.ShowDialog<bool>(this))
            {
                _confirmedClose = true;
                Close();
            }

            return;
        }

        // Jobs are running: continue in the background (main action), cancel them and exit, or stay.
        var dialog = new ConfirmWindow(Loc.T("ConfirmExitTitle"), Loc.T("ConfirmExitBackgroundText"), Loc.T("BtnContinueInBackground"),
                danger: false, confirmIcon: "IconTrayArrowDown")
            .WithAlternative(Loc.T("BtnCancelJobsAndExit"), "IconClose");
        var keepRunning = await dialog.ShowDialog<bool>(this);
        if (dialog.AlternativeChosen)
        {
            _confirmedClose = true;
            Close();
        }
        else if (keepRunning)
        {
            vm.HideToTray();
        }
    }
}
