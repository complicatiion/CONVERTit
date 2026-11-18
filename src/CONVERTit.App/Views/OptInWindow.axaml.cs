using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.App.ViewModels;

namespace CONVERTit.App.Views;

/// <summary>Something the user agrees to download: name, short description, license and links.</summary>
public sealed record OptInItem(string Name, string Description, string License, string Homepage, string LicenseFile);

/// <summary>
/// Opt-in dialog for downloads (modules, updates): lists what is loaded from where, with a globe button to the
/// original project and a button for the license text. "Download & install" is enabled only after the user
/// ticks "I understand"; Cancel left, main action right.
/// </summary>
public partial class OptInWindow : Window
{
    public OptInWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this);
        UnderstandBox.IsCheckedChanged += (_, _) => ConfirmButton.IsEnabled = UnderstandBox.IsChecked == true;
    }

    public OptInWindow(string title, string message, IReadOnlyList<OptInItem> items, string confirmText, string? understandText = null) : this()
    {
        Title = title;
        HeaderText.Text = title;
        MessageText.Text = message;
        ConfirmLabel.Text = confirmText;
        UnderstandText.Text = understandText ?? Loc.T("OptInUnderstand");
        foreach (var item in items)
        {
            ItemsPanel.Children.Add(Row(item));
        }
    }

    private Control Row(OptInItem item)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = item.Name, FontWeight = FontWeight.SemiBold });
        if (item.Description.Length > 0)
        {
            text.Children.Add(new TextBlock { Text = item.Description, TextWrapping = TextWrapping.Wrap, Classes = { "small", "muted" } });
        }

        grid.Children.Add(text);
        var pill = new Border
        {
            Classes = { "pill", "neutral" },
            Margin = new Avalonia.Thickness(10, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = item.License, Classes = { "small" } }
        };
        Grid.SetColumn(pill, 1);
        grid.Children.Add(pill);

        var globe = IconButton("IconWeb", Loc.T("TipProjectPage"), (_, _) => AppEnvironment.OpenUrl(item.Homepage));
        Grid.SetColumn(globe, 2);
        grid.Children.Add(globe);

        var license = IconButton("IconDocument", Loc.T("BtnShowLicense"), async (_, _) => await ShowLicenseAsync(item));
        Grid.SetColumn(license, 3);
        grid.Children.Add(license);
        return grid;
    }

    private static Button IconButton(string icon, string tip, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button
        {
            Classes = { "icon", "small" },
            Content = new PathIcon { Data = Res.Icon(icon) },
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, tip);
        button.Click += click;
        return button;
    }

    private async Task ShowLicenseAsync(OptInItem item)
    {
        var path = Path.Combine(AppEnvironment.LicensesDirectory, item.LicenseFile.Replace('/', Path.DirectorySeparatorChar));
        string text;
        try
        {
            text = File.Exists(path) ? await File.ReadAllTextAsync(path) : Loc.F("LicenseFileMissing", path);
        }
        catch (Exception ex)
        {
            text = ex.Message;
        }

        await new TextWindow(item.Name, text).ShowDialog(this);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (UnderstandBox.IsChecked == true)
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
