using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CONVERTit.App.Controls;

/// <summary>
/// Timeline of the trim pages: full track, selected range (start–end) in the accent color and the playback
/// position as a marker. Clicking or dragging sets the position.
/// </summary>
public sealed class RangeBar : Control
{
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<RangeBar, double>(nameof(Maximum), 1);
    public static readonly StyledProperty<double> StartProperty = AvaloniaProperty.Register<RangeBar, double>(nameof(Start));
    public static readonly StyledProperty<double> EndProperty = AvaloniaProperty.Register<RangeBar, double>(nameof(End));
    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<RangeBar, double>(nameof(Position), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<RangeBar, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> RangeBrushProperty = AvaloniaProperty.Register<RangeBar, IBrush?>(nameof(RangeBrush));
    public static readonly StyledProperty<IBrush?> MarkerBrushProperty = AvaloniaProperty.Register<RangeBar, IBrush?>(nameof(MarkerBrush));

    private bool _dragging;

    static RangeBar()
    {
        AffectsRender<RangeBar>(MaximumProperty, StartProperty, EndProperty, PositionProperty, TrackBrushProperty, RangeBrushProperty, MarkerBrushProperty);
    }

    public RangeBar()
    {
        Height = 22;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Start { get => GetValue(StartProperty); set => SetValue(StartProperty, value); }

    /// <summary>End of the range; 0 = end of the file.</summary>
    public double End { get => GetValue(EndProperty); set => SetValue(EndProperty, value); }

    public double Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? RangeBrush { get => GetValue(RangeBrushProperty); set => SetValue(RangeBrushProperty, value); }
    public IBrush? MarkerBrush { get => GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var max = Maximum > 0 ? Maximum : 1;
        var trackTop = h / 2 - 4;
        context.DrawRectangle(TrackBrush ?? Brushes.Gray, null, new RoundedRect(new Rect(0, trackTop, w, 8), 4));

        var start = Math.Clamp(Start / max, 0, 1) * w;
        var end = (End > 0 ? Math.Clamp(End / max, 0, 1) : 1) * w;
        if (end > start)
        {
            context.DrawRectangle(RangeBrush ?? Brushes.YellowGreen, null, new RoundedRect(new Rect(start, trackTop, end - start, 8), 4));
            // Handles at both ends of the range.
            var handle = RangeBrush ?? Brushes.YellowGreen;
            context.DrawRectangle(handle, null, new RoundedRect(new Rect(Math.Max(0, start - 1.5), 2, 3, h - 4), 1.5));
            context.DrawRectangle(handle, null, new RoundedRect(new Rect(Math.Min(w - 3, end - 1.5), 2, 3, h - 4), 1.5));
        }

        var x = Math.Clamp(Position / max, 0, 1) * w;
        var marker = MarkerBrush ?? Brushes.White;
        context.DrawEllipse(marker, null, new Point(x, h / 2), 6, 6);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        e.Pointer.Capture(this);
        SetFromPointer(e.GetPosition(this).X);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
        {
            SetFromPointer(e.GetPosition(this).X);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void SetFromPointer(double x)
    {
        if (Bounds.Width <= 0 || Maximum <= 0)
        {
            return;
        }

        Position = Math.Clamp(x / Bounds.Width, 0, 1) * Maximum;
    }
}
