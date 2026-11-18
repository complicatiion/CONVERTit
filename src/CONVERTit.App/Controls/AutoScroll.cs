using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CONVERTit.App.Controls;

/// <summary>
/// Middle-click scrolling like in a web browser for every ScrollViewer: click the mouse wheel and move the mouse
/// up or down – the further from the starting point, the faster it scrolls. Holding the wheel button and
/// dragging works too (releasing ends it); after a short click the mode stays on until the next click or Esc.
/// The innermost ScrollViewer that can scroll in that direction takes it.
/// </summary>
public static class AutoScroll
{
    private const double DeadZone = 10;
    private const double DragThreshold = 6;

    private static ScrollViewer? _target;
    private static TopLevel? _top;
    private static Point _origin;
    private static Point _pointer;
    private static bool _moved;
    private static Cursor? _previousCursor;
    private static DispatcherTimer? _timer;

    private static RoutedEventArgs? _startEvent;

    /// <summary>Registers the class handler once (App start).</summary>
    public static void Install()
    {
        InputElement.PointerPressedEvent.AddClassHandler<ScrollViewer>(OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// Bubbling reaches the innermost ScrollViewer first; it takes the press when it can scroll, outer ones see the
    /// same event afterwards and ignore it. Handled events count too (list items mark presses as handled).
    /// </summary>
    private static void OnPressed(ScrollViewer viewer, PointerPressedEventArgs e)
    {
        if (_target is not null || ReferenceEquals(_startEvent, e) || !e.GetCurrentPoint(viewer).Properties.IsMiddleButtonPressed
            || !CanScroll(viewer))
        {
            return;
        }

        _startEvent = e;
        Start(viewer, e);
        e.Handled = true;
    }

    /// <summary>While scrolling, the next click anywhere ends it and is not passed on.</summary>
    private static void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(_startEvent, e))
        {
            return;
        }

        Stop();
        e.Handled = true;
    }

    private static bool CanScroll(ScrollViewer viewer) =>
        viewer.Extent.Height > viewer.Viewport.Height + 1 || viewer.Extent.Width > viewer.Viewport.Width + 1;

    private static void Start(ScrollViewer viewer, PointerPressedEventArgs e)
    {
        _top = TopLevel.GetTopLevel(viewer);
        if (_top is null)
        {
            return;
        }

        _target = viewer;
        _origin = _pointer = e.GetPosition(_top);
        _moved = false;
        _previousCursor = viewer.Cursor;
        viewer.Cursor = new Cursor(StandardCursorType.SizeAll);
        _top.AddHandler(InputElement.PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel, handledEventsToo: true);
        _top.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        _top.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        _top.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        _top.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        _timer.Start();
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_top is null)
        {
            return;
        }

        _pointer = e.GetPosition(_top);
        if (Math.Abs(_pointer.Y - _origin.Y) > DragThreshold || Math.Abs(_pointer.X - _origin.X) > DragThreshold)
        {
            _moved = true;
        }
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Hold-and-drag: releasing the wheel button after moving ends it; a short click keeps the mode on.
        if (e.InitialPressMouseButton == MouseButton.Middle && _moved)
        {
            Stop();
        }
    }

    private static void OnKey(object? sender, KeyEventArgs e)
    {
        Stop();
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
        }
    }

    private static void OnWheel(object? sender, PointerWheelEventArgs e) => Stop();

    private static void Tick()
    {
        if (_target is null || !_target.IsEffectivelyVisible || _target.GetVisualRoot() is null)
        {
            Stop();
            return;
        }

        static double Speed(double distance)
        {
            var d = Math.Abs(distance) - DeadZone;
            if (d <= 0)
            {
                return 0;
            }

            // Grows faster the further away (≈ 1 px per frame close to the dead zone, ≈ 40 px at 250 px).
            return Math.Sign(distance) * Math.Min(60, d * d / 1500 + d / 20);
        }

        var dy = Speed(_pointer.Y - _origin.Y);
        var dx = Speed(_pointer.X - _origin.X);
        if (dx == 0 && dy == 0)
        {
            return;
        }

        var offset = _target.Offset;
        var maxX = Math.Max(0, _target.Extent.Width - _target.Viewport.Width);
        var maxY = Math.Max(0, _target.Extent.Height - _target.Viewport.Height);
        _target.Offset = new Vector(Math.Clamp(offset.X + dx, 0, maxX), Math.Clamp(offset.Y + dy, 0, maxY));
    }

    private static void Stop()
    {
        _timer?.Stop();
        if (_top is not null)
        {
            _top.RemoveHandler(InputElement.PointerPressedEvent, OnAnyPress);
            _top.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
            _top.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
            _top.RemoveHandler(InputElement.KeyDownEvent, OnKey);
            _top.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        }

        if (_target is not null)
        {
            _target.Cursor = _previousCursor;
        }

        _target = null;
        _top = null;
    }
}
