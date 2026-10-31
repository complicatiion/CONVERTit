using Avalonia.Controls;
using Avalonia.Interactivity;
using CONVERTit.App.ViewModels;

namespace CONVERTit.App.Views;

/// <summary>Confirmation dialog: Cancel left, main action right.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
        CONVERTit.App.Services.WindowChrome.Apply(this);
    }

    public ConfirmWindow(string title, string message, string confirmText, bool danger, string? confirmIcon = null) : this()
    {
        Title = title;
        HeaderText.Text = title;
        MessageText.Text = message;
        ConfirmLabel.Text = confirmText;
        ConfirmLabel.Icon = Res.Icon(confirmIcon ?? (danger ? "IconDelete" : "IconCheck"));
        Glyph.Data = Res.Icon(danger ? "IconAlert" : "IconInfo");
        Glyph.Foreground = Res.Brush(danger ? "BrushDanger" : "BrushAccentIcon");
        if (danger)
        {
            ConfirmButton.Classes.Remove("primary");
            ConfirmButton.Classes.Add("danger");
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
