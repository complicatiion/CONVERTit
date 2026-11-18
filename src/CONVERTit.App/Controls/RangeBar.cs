using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CONVERTit.Core.Ffmpeg;

namespace CONVERTit.App.Controls;

/// <summary>
/// Timeline of the trim pages: full track, selected range (start–end) in the accent color and the playback
/// position as a marker. Clicking or dragging sets the position; the start and end handles can be dragged.
/// With multi-cut segments the kept parts are drawn in the accent color, removed parts in red, the selected part
/// with an outline; cut lines can be dragged.
/// </summary>
public sealed class RangeBar : Control
{
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<RangeBar, double>(nameof(Maximum), 1);
    public static readonly StyledProperty<double> StartProperty =
        AvaloniaProperty.Register<RangeBar, double>(nameof(Start), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> EndProperty =
        AvaloniaProperty.Register<RangeBar, double>(nameof(End), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<RangeBar, double>(nameof(Position), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<RangeBar, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> RangeBrushProperty = AvaloniaProperty.Register<RangeBar, IBrush?>(nameof(RangeBrush));
    public static readonly StyledProperty<IBrush?> MarkerBrushProperty = AvaloniaProperty.Register<RangeBar, IBrush?>(nameof(MarkerBrush));

    /// <summary>Ring around the playhead (card background), separates it from the track.</summary>
    public static readonly StyledProperty<IBrush?> OutlineBrushProperty = AvaloniaProperty.Register<RangeBar, IBrush?>(nameof(OutlineBrush));
    public static readonly StyledProperty<IBrush?> RemovedBrushProperty = AvaloniaProperty.Register<RangeBar, IBrush?>(nameof(RemovedBrush));
    public static readonly StyledProperty<IReadOnlyList<TrimSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<RangeBar, IReadOnlyList<TrimSegment>?>(nameof(Segments));

    /// <summary>Changes whenever the segments changed (the list itself stays the same instance).</summary>
    public static readonly StyledProperty<int> SegmentsVersionProperty = AvaloniaProperty.Register<RangeBar, int>(nameof(SegmentsVersion));

    public static readonly StyledProperty<int> SelectedSegmentProperty =
        AvaloniaProperty.Register<RangeBar, int>(nameof(SelectedSegment), -1, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private const double HandleHit = 7;

    private enum Drag { None, Position, Start, End, Cut }

    private Drag _drag;
    private int _dragCut;

    static RangeBar()
    {
        AffectsRender<RangeBar>(MaximumProperty, StartProperty, EndProperty, PositionProperty, TrackBrushProperty, RangeBrushProperty,
            MarkerBrushProperty, OutlineBrushProperty, RemovedBrushProperty, SegmentsProperty, SegmentsVersionProperty, SelectedSegmentProperty);
    }

    public RangeBar()
    {
        Height = 30;
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

    public IBrush? OutlineBrush { get => GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }
    public IBrush? RemovedBrush { get => GetValue(RemovedBrushProperty); set => SetValue(RemovedBrushProperty, value); }
    public IReadOnlyList<TrimSegment>? Segments { get => GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public int SegmentsVersion { get => GetValue(SegmentsVersionProperty); set => SetValue(SegmentsVersionProperty, value); }
    public int SelectedSegment { get => GetValue(SelectedSegmentProperty); set => SetValue(SelectedSegmentProperty, value); }

    /// <summary>A cut line was dragged: cut index (1 … segments-1) and the new time.</summary>
    public event Action<int, double>? CutMoved;

    /// <summary>Time under the pointer of the last press (for the right-click menu).</summary>
    public double PressedTime { get; private set; }

    private bool HasSegments => Segments is { Count: > 0 };

    private double X(double time) => Math.Clamp(time / (Maximum > 0 ? Maximum : 1), 0, 1) * Bounds.Width;

    private double Time(double x) => Bounds.Width <= 0 || Maximum <= 0 ? 0 : Math.Clamp(x / Bounds.Width, 0, 1) * Maximum;

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var trackTop = h / 2 - 5;
        context.DrawRectangle(TrackBrush ?? Brushes.Gray, null, new RoundedRect(new Rect(0, trackTop, w, 10), 5));
        var accent = RangeBrush ?? Brushes.YellowGreen;
        var marker = MarkerBrush ?? Brushes.White;

        if (HasSegments)
        {
            var removed = RemovedBrush ?? Brushes.IndianRed;
            var outline = new Pen(marker, 1.5);
            for (var i = 0; i < Segments!.Count; i++)
            {
                var s = Segments[i];
                var x0 = X(s.Start);
                var x1 = X(s.End);
                if (x1 - x0 < 1)
                {
                    continue;
                }

                var rect = new Rect(x0 + 1, trackTop, x1 - x0 - 2, 10);
                context.DrawRectangle(s.Removed ? removed : accent, i == SelectedSegment ? outline : null, new RoundedRect(rect, 3));
            }

            // Cut lines (draggable).
            for (var i = 1; i < Segments.Count; i++)
            {
                var x = X(Segments[i].Start);
                context.DrawRectangle(marker, null, new RoundedRect(new Rect(x - 1, 1, 2, h - 2), 1));
            }
        }
        else
        {
            var start = X(Start);
            var end = End > 0 ? X(End) : w;
            if (end > start)
            {
                context.DrawRectangle(accent, null, new RoundedRect(new Rect(start, trackTop, end - start, 10), 5));
                // Handles at both ends of the range (draggable).
                context.DrawRectangle(accent, null, new RoundedRect(new Rect(Math.Max(0, start - 2), 1, 4, h - 2), 2));
                context.DrawRectangle(accent, null, new RoundedRect(new Rect(Math.Min(w - 4, end - 2), 1, 4, h - 2), 2));
            }
        }

        // Playhead: a line over the full height plus a larger knob with a ring in the card color, so it stands out
        // from the accent range and from the white cut lines.
        var px = X(Position);
        var ring = new Pen(OutlineBrush ?? Brushes.Black, 2.5);
        context.DrawRectangle(ring.Brush, null, new RoundedRect(new Rect(px - 2.5, 0, 5, h), 2.5));
        context.DrawRectangle(marker, null, new RoundedRect(new Rect(px - 1.5, 0, 3, h), 1.5));
        context.DrawEllipse(marker, ring, new Point(px, h / 2), 8, 8);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var x = e.GetPosition(this).X;
        PressedTime = Time(x);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Right click: the menu (cut here, remove segment …) uses PressedTime.
            if (HasSegments)
            {
                SelectedSegment = CutList.IndexAt(Segments!, PressedTime);
            }

            return;
        }

        _drag = HitTest(x);
        e.Pointer.Capture(this);
        if (_drag == Drag.Position)
        {
            Position = PressedTime;
            if (HasSegments)
            {
                SelectedSegment = CutList.IndexAt(Segments!, PressedTime);
            }
        }
    }

    private Drag HitTest(double x)
    {
        if (HasSegments)
        {
            for (var i = 1; i < Segments!.Count; i++)
            {
                if (Math.Abs(X(Segments[i].Start) - x) <= HandleHit)
                {
                    _dragCut = i;
                    return Drag.Cut;
                }
            }

            return Drag.Position;
        }

        var hasRange = Start > 0 || End > 0;
        if (hasRange && Math.Abs(X(Start) - x) <= HandleHit)
        {
            return Drag.Start;
        }

        if (End > 0 && Math.Abs(X(End) - x) <= HandleHit)
        {
            return Drag.End;
        }

        return Drag.Position;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var x = e.GetPosition(this).X;
        if (_drag == Drag.None)
        {
            // Resize cursor over draggable lines.
            Cursor = new Cursor(HitTest(x) is Drag.Cut or Drag.Start or Drag.End ? StandardCursorType.SizeWestEast : StandardCursorType.Hand);
            return;
        }

        var time = Time(x);
        switch (_drag)
        {
            case Drag.Position:
                Position = time;
                break;
            case Drag.Start:
                Start = End > 0 ? Math.Min(time, End - CutList.MinLength) : time;
                Position = Start;
                break;
            case Drag.End:
                End = Math.Max(time, Start + CutList.MinLength);
                Position = End;
                break;
            case Drag.Cut:
                CutMoved?.Invoke(_dragCut, time);
                InvalidateVisual();
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = Drag.None;
        e.Pointer.Capture(null);
    }
}
