using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
            ApplyDisplayProfile();
            FitToScreen();
            // Maximized by default (Settings › Appearance › Start maximized).
            if (ViewModel?.Settings.StartMaximized != false)
            {
                WindowState = WindowState.Maximized;
            }
        };
        ScalingChanged += (_, _) => ApplyDisplayProfile();

        // Files and folders can be dropped anywhere on the window.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

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

        if (_confirmedClose || ViewModel is not { } vm || !vm.Queue.IsBusy
            || e.CloseReason is WindowCloseReason.OSShutdown)
        {
            return;
        }

        // Jobs are running: ask before cancelling them.
        e.Cancel = true;
        var dialog = new ConfirmWindow(Loc.T("ConfirmExitTitle"), Loc.T("ConfirmExitText"), Loc.T("BtnExitAnyway"), danger: true, confirmIcon: "IconClose");
        if (await dialog.ShowDialog<bool>(this))
        {
            _confirmedClose = true;
            Close();
        }
    }
}
