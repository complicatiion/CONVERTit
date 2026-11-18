using Avalonia;
using Avalonia.Controls;

namespace CONVERTit.App.Controls;

/// <summary>
/// Like a horizontal WrapPanel, but every line is centered: buttons are laid out from the middle outwards in the
/// first and in every further line (a single button in the second line sits in the center, not on the left).
/// </summary>
public sealed class CenterWrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var available = new Size(availableSize.Width, double.PositiveInfinity);
        double lineWidth = 0, lineHeight = 0, width = 0, height = 0;
        foreach (var child in Children)
        {
            child.Measure(available);
            var size = child.DesiredSize;
            if (lineWidth > 0 && lineWidth + size.Width > availableSize.Width)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight;
                lineWidth = 0;
                lineHeight = 0;
            }

            lineWidth += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        width = Math.Max(width, lineWidth);
        height += lineHeight;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var line = new List<Control>();
        double lineWidth = 0, lineHeight = 0, y = 0;

        void Flush()
        {
            var x = Math.Max(0, (finalSize.Width - lineWidth) / 2);
            foreach (var c in line)
            {
                c.Arrange(new Rect(x, y, c.DesiredSize.Width, lineHeight));
                x += c.DesiredSize.Width;
            }

            y += lineHeight;
            line.Clear();
            lineWidth = 0;
            lineHeight = 0;
        }

        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (line.Count > 0 && lineWidth + size.Width > finalSize.Width)
            {
                Flush();
            }

            line.Add(child);
            lineWidth += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        if (line.Count > 0)
        {
            Flush();
        }

        return finalSize;
    }
}
