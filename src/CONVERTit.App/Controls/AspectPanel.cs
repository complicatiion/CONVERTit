using Avalonia;
using Avalonia.Controls;

namespace CONVERTit.App.Controls;

/// <summary>
/// Gives its child exactly the size of the video picture (uniform fit, centered). Native video surfaces (LibVLC)
/// paint black wherever they have no picture; sized like this the area around the video shows the card
/// background of the current theme instead of black bars. Aspect ratio 0 = fill.
/// </summary>
public sealed class AspectPanel : Panel
{
    public static readonly StyledProperty<double> AspectRatioProperty = AvaloniaProperty.Register<AspectPanel, double>(nameof(AspectRatio));

    static AspectPanel()
    {
        AffectsArrange<AspectPanel>(AspectRatioProperty);
        AffectsMeasure<AspectPanel>(AspectRatioProperty);
    }

    /// <summary>Width / height of the picture.</summary>
    public double AspectRatio { get => GetValue(AspectRatioProperty); set => SetValue(AspectRatioProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Fit(availableSize));
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = Fit(finalSize);
        var rect = new Rect((finalSize.Width - size.Width) / 2, (finalSize.Height - size.Height) / 2, size.Width, size.Height);
        foreach (var child in Children)
        {
            child.Arrange(rect);
        }

        return finalSize;
    }

    private Size Fit(Size box)
    {
        if (AspectRatio <= 0 || double.IsInfinity(box.Width) || double.IsInfinity(box.Height) || box.Width <= 0 || box.Height <= 0)
        {
            return box;
        }

        var width = box.Width;
        var height = width / AspectRatio;
        if (height > box.Height)
        {
            height = box.Height;
            width = height * AspectRatio;
        }

        // Whole pixels: a native child window rounds anyway, this avoids a 1 px black seam.
        return new Size(Math.Floor(width), Math.Floor(height));
    }
}
