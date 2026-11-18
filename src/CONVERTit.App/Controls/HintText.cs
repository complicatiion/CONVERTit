using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using CONVERTit.App.Localization;

namespace CONVERTit.App.Controls;

/// <summary>
/// Short hint below options, introduced by a small round "i" in the accent color, so users see at a glance that it
/// is only additional information. Settings › General › Show hints hides all hints (class "keep" stays visible,
/// e.g. warnings and the FFmpeg setup note).
/// </summary>
public sealed class HintText : TextBlock
{
    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<HintText, string?>(nameof(Hint));

    static HintText()
    {
        HintProperty.Changed.AddClassHandler<HintText>((control, _) => control.Rebuild());
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LocState.Instance.PropertyChanged += OnLanguageChanged;
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LocState.Instance.PropertyChanged -= OnLanguageChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        var icon = new PathIcon
        {
            Width = 13,
            Height = 13,
            Margin = new Thickness(0, 0, 6, -2),
            Data = Application.Current?.FindResource("IconInfo") as Geometry
        };
        icon.Bind(TemplatedControl.ForegroundProperty, icon.GetResourceObservable("BrushAccentIcon"));
        Inlines = new InlineCollection
        {
            new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center },
            new Run(Hint ?? "")
        };
    }
}
