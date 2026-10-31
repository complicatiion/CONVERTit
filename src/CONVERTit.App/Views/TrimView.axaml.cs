using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using CONVERTit.App.ViewModels;

namespace CONVERTit.App.Views;

public partial class TrimView : UserControl
{
    private TrimViewModel? _vm;

    public TrimView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as TrimViewModel);

        // Shortcuts: Space play/pause · I start · O end · ←/→ ±1 s (Shift ±10 s). Not while typing in a text box.
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
