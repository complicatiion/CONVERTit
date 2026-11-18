using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CONVERTit.App.Localization;
using CONVERTit.App.ViewModels;

namespace CONVERTit.App.Views;

public partial class TrimView : UserControl
{
    private TrimViewModel? _vm;

    public TrimView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as TrimViewModel);

        // Shortcuts: Space play/pause · I start · O end · S/C cut · Del remove segment · ←/→ ±1 s (Shift ±10 s).
        // Not while typing in a text box.
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Timeline.ContextRequested += OnTimelineContext;
        VideoClickLayer.PointerPressed += OnVideoClick;
        Timeline.CutMoved += (cut, time) => _vm?.MoveCut(cut, time);
    }

    private void Attach(TrimViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelChanged;
        }

        _vm = vm;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelChanged;
            Video.MediaPlayer = _vm.Player;
        }
    }

    /// <summary>Right-click on the timeline: cut here, remove/restore the segment, keep only it, remove the cut, clear cuts.</summary>
    private void OnTimelineContext(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm is null || !_vm.HasSource)
        {
            return;
        }

        var time = Timeline.PressedTime;
        var items = new List<Control>
        {
            Item(Loc.T("BtnCutHere"), "IconTrim", () => _vm.CutAt(time))
        };
        if (_vm.HasSelectedSegment)
        {
            items.Add(Item(Loc.T(_vm.SelectedSegmentRemoved ? "CtxRestoreSegment" : "CtxRemoveSegment"), _vm.SelectedSegmentRemoved ? "IconUndo" : "IconDelete",
                () => _vm.ToggleSegmentCommand.Execute(null), danger: !_vm.SelectedSegmentRemoved));
            items.Add(Item(Loc.T("CtxKeepOnlySegment"), "IconCrop", () => _vm.KeepOnlySegmentCommand.Execute(null)));
        }

        if (_vm.HasCuts)
        {
            items.Add(new Separator());
            items.Add(Item(Loc.T("CtxRemoveCut"), "IconClose", () => _vm.RemoveCutNear(time), danger: true));
            items.Add(Item(Loc.T("BtnClearCuts"), "IconBroom", () => _vm.ClearCutsCommand.Execute(null), danger: true));
        }

        new ContextMenu { ItemsSource = items }.Open(Timeline);
        e.Handled = true;
    }

    private static MenuItem Item(string text, string icon, Action run, bool danger = false)
    {
        var item = new MenuItem { Header = text, Icon = new PathIcon { Data = Res.Icon(icon), Width = 16, Height = 16 } };
        if (danger)
        {
            item.Classes.Add("danger");
        }

        item.Click += (_, _) => run();
        return item;
    }

    private DispatcherTimer? _indicatorTimer;

    /// <summary>Click on the video: play/pause and a short symbol in the middle (like a web player).</summary>
    private void OnVideoClick(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || !_vm.HasPlayer || !e.GetCurrentPoint(VideoClickLayer).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _vm.PlayPauseCommand.Execute(null);
        _vm.ShowClickIndicator = true;
        _indicatorTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(650), DispatcherPriority.Normal, (_, _) =>
        {
            _indicatorTimer?.Stop();
            if (_vm is not null)
            {
                _vm.ShowClickIndicator = false;
            }
        });
        _indicatorTimer.Stop();
        _indicatorTimer.Start();
        e.Handled = true;
        // The click layer lives in the video's overlay window; give the keyboard back to the main window (Space, S, C …).
        Dispatcher.UIThread.Post(() => (TopLevel.GetTopLevel(this) as Window)?.Activate());
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrimViewModel.Player) && _vm is not null)
        {
            Video.MediaPlayer = _vm.Player;
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.Source is TextBox || !_vm.HasSource)
        {
            return;
        }

        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Space:
                _vm.PlayPauseCommand.Execute(null);
                break;
            case Key.I:
                _vm.SetStartCommand.Execute(null);
                break;
            case Key.O:
                _vm.SetEndCommand.Execute(null);
                break;
            case Key.S or Key.C when e.KeyModifiers == KeyModifiers.None:
                _vm.CutHereCommand.Execute(null);
                break;
            case Key.Delete:
                _vm.ToggleSegmentCommand.Execute(null);
                break;
            case Key.Left:
                _vm.StepCommand.Execute((-step).ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case Key.Right:
                _vm.StepCommand.Execute(step.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
