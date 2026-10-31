using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using CONVERTit.App.Localization;

namespace CONVERTit.App.Controls;

/// <summary>
/// Short hint below options: "Note: …" / "Hinweis: …" with the prefix in semibold, so users see at a glance that
/// it is only additional information. The prefix follows the language.
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
        Inlines = new InlineCollection
        {
            new Run(Loc.T("NotePrefix") + " ") { FontWeight = FontWeight.SemiBold },
            new Run(Hint ?? "")
        };
    }
}
