using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CONVERTit.App.Views;

/// <summary>Read-only text viewer (licenses, legal notice).</summary>
public partial class TextWindow : Window
{
    public TextWindow()
    {
        InitializeComponent();
        CONVERTit.App.Services.WindowChrome.Apply(this);
    }

    public TextWindow(string title, string text) : this()
    {
        Title = title;
        HeaderText.Text = title;
        Body.Text = text;
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(Body.Text ?? "");
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
