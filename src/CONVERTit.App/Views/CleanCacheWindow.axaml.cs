using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.Core.Storage;
using CONVERTit.Platform.Windows;

namespace CONVERTit.App.Views;

/// <summary>What the user chose to clean.</summary>
public sealed record CleanCacheChoice(bool Thumbnails, bool Icons, bool OwnPreviews, bool RestartExplorer);

/// <summary>Opt-in dialog for resetting the Explorer caches; shows the current size of every cache.</summary>
public partial class CleanCacheWindow : Window
{
    public CleanCacheWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this);
        Title = Loc.T("CleanCacheTitle");
        var (thumbCount, thumbBytes) = ShellCacheCleaner.Measure(ShellCacheKind.Thumbnails);
        var (iconCount, iconBytes) = ShellCacheCleaner.Measure(ShellCacheKind.Icons);
        ThumbnailsInfo.Text = Loc.F("CleanSize", thumbCount, Units.Bytes(thumbBytes));
        IconsInfo.Text = Loc.F("CleanSize", iconCount, Units.Bytes(iconBytes)) + " · " + Loc.T("CleanIconsHint");
        OwnInfo.Text = Units.Bytes(ThumbnailService.CacheSize());
        foreach (var toggle in new ToggleButton[] { ThumbnailsSwitch, IconsSwitch, OwnSwitch })
        {
            toggle.IsCheckedChanged += (_, _) => Update();
        }

        UnderstandBox.IsCheckedChanged += (_, _) => Update();
    }

    private bool AnyChosen => ThumbnailsSwitch.IsChecked == true || IconsSwitch.IsChecked == true || OwnSwitch.IsChecked == true;

    private void Update() => ConfirmButton.IsEnabled = AnyChosen && UnderstandBox.IsChecked == true;

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (!AnyChosen || UnderstandBox.IsChecked != true)
        {
            return;
        }

        Close(new CleanCacheChoice(ThumbnailsSwitch.IsChecked == true, IconsSwitch.IsChecked == true, OwnSwitch.IsChecked == true,
            RestartSwitch.IsChecked == true));
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
