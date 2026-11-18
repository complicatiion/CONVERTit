using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CONVERTit.App.ViewModels;

namespace CONVERTit.App.Views;

/// <summary>Mini editor: dragging the preview moves the motif, the mouse wheel changes its size.</summary>
public partial class AppIconsView : UserControl
{
    private Point? _last;

    public AppIconsView()
    {
        InitializeComponent();
        PreviewArea.PointerPressed += (_, e) =>
        {
            _last = e.GetPosition(PreviewArea);
            e.Pointer.Capture(PreviewArea);
        };
        PreviewArea.PointerMoved += (_, e) =>
        {
            if (_last is not { } last || DataContext is not AppIconsViewModel vm)
            {
                return;
            }

            var now = e.GetPosition(PreviewArea);
            // One preview tile is 160 px wide: moving by a tile width shifts the motif by 100 %.
            var dx = (now.X - last.X) / 160.0;
            var dy = (now.Y - last.Y) / 160.0;
            if (Math.Abs(dx) >= 0.01 || Math.Abs(dy) >= 0.01)
            {
                vm.Nudge(dx, dy);
                _last = now;
            }
        };
        PreviewArea.PointerReleased += (_, e) =>
        {
            _last = null;
            e.Pointer.Capture(null);
        };
        PreviewArea.PointerWheelChanged += (_, e) =>
        {
            if (DataContext is AppIconsViewModel vm)
            {
                vm.Zoom(e.Delta.Y > 0 ? 1 : -1);
                e.Handled = true;
            }
        };
    }
}
