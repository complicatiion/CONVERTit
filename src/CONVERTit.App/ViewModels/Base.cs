using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.Core.Storage;

namespace CONVERTit.App.ViewModels;

/// <summary>Theme resources (brushes, icons) for view models; follows the current light/dark variant.</summary>
public static class Res
{
    public static IBrush Brush(string key)
    {
        if (Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    public static Geometry? Icon(string key)
    {
        if (Application.Current is { } app && app.TryGetResource(key, null, out var value) && value is Geometry geometry)
        {
            return geometry;
        }

        return null;
    }
}

/// <summary>Base of all pages.</summary>
public abstract class PageViewModel(MainViewModel main) : ObservableObject
{
    public MainViewModel Main { get; } = main;

    protected UiHost Host => Main.Host;

    public abstract string TitleKey { get; }

    public abstract string IconKey { get; }

    /// <summary>Key under which the page's options are saved.</summary>
    public virtual string StateKey => GetType().Name.Replace("ViewModel", "");

    public string Title => Loc.T(TitleKey);

    public Geometry? Icon => Res.Icon(IconKey);

    /// <summary>Group title for the breadcrumb ("Video"), empty for top-level pages.</summary>
    public string GroupKey { get; set; } = "";

    public string GroupTitle => GroupKey.Length > 0 ? Loc.T(GroupKey) : "";

    public bool HasGroup => GroupKey.Length > 0;

    public virtual void OnActivated()
    {
    }

    public virtual void OnLanguageChanged()
    {
        // Refreshes every binding of the page (titles, choice lists).
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Files dropped on the window while this page is shown; false = not handled.</summary>
    public virtual bool AcceptDrop(IReadOnlyList<string> paths) => false;

    public virtual void SaveState(Dictionary<string, JsonElement> pages)
    {
    }

    public virtual void LoadState(Dictionary<string, JsonElement> pages)
    {
    }

    /// <summary>Saves several option objects under one key.</summary>
    protected static void Save(Dictionary<string, JsonElement> pages, string key, params object[] parts)
    {
        var map = new Dictionary<string, JsonElement>();
        for (var i = 0; i < parts.Length; i++)
        {
            map[parts[i].GetType().Name + i] = JsonSerializer.SerializeToElement(parts[i], parts[i].GetType(), JsonFile.Options);
        }

        pages[key] = JsonSerializer.SerializeToElement(map, JsonFile.Options);
    }

    /// <summary>Restores option objects saved with <see cref="Save"/> (same order).</summary>
    protected static void Load(Dictionary<string, JsonElement> pages, string key, params object[] parts)
    {
        if (!pages.TryGetValue(key, out var element))
        {
            return;
        }

        try
        {
            var map = element.Deserialize<Dictionary<string, JsonElement>>(JsonFile.Options);
            if (map is null)
            {
                return;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                if (map.TryGetValue(parts[i].GetType().Name + i, out var value))
                {
                    var loaded = value.Deserialize(parts[i].GetType(), JsonFile.Options);
                    if (loaded is null)
                    {
                        continue;
                    }

                    foreach (var property in parts[i].GetType().GetProperties())
                    {
                        if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                        {
                            property.SetValue(parts[i], property.GetValue(loaded));
                        }
                    }
                }
            }
        }
        catch
        {
            // Saved state from another version: keep defaults.
        }
    }
}

/// <summary>Sidebar entry of a page.</summary>
public sealed partial class NavItem : ObservableObject
{
    public NavItem(PageViewModel page)
    {
        Page = page;
    }

    public PageViewModel Page { get; }

    public NavGroup? Group { get; set; }

    public Geometry? Icon => Page.Icon;

    public string Title => Page.Title;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Counter at the right (queue).</summary>
    [ObservableProperty] private string _badge = "";

    public bool HasBadge => Badge.Length > 0;

    public bool IsChild => Group is not null;

    partial void OnBadgeChanged(string value) => OnPropertyChanged(nameof(HasBadge));

    public void RefreshText() => OnPropertyChanged(nameof(Title));
}

/// <summary>Sidebar group (Video, Audio, Image) with sub pages.</summary>
public sealed partial class NavGroup : ObservableObject
{
    private readonly MainViewModel _main;

    public NavGroup(MainViewModel main, string key, string titleKey, string iconKey, IEnumerable<NavItem> items)
    {
        _main = main;
        Key = key;
        TitleKey = titleKey;
        Icon = Res.Icon(iconKey);
        Items = items.ToList();
        foreach (var item in Items)
        {
            item.Group = this;
            item.Page.GroupKey = titleKey;
        }
    }

    public string Key { get; }

    public string TitleKey { get; }

    public string Title => Loc.T(TitleKey);

    public Geometry? Icon { get; }

    public IReadOnlyList<NavItem> Items { get; }

    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>Collapsed sidebar: sub pages shown as icons below the group (own state, starts closed).</summary>
    [ObservableProperty] private bool _isRailExpanded;

    /// <summary>A sub page of this group is open.</summary>
    [ObservableProperty] private bool _containsSelection;

    public bool ShowChildren => _main.IsSidebarCollapsed ? IsRailExpanded : IsExpanded;

    /// <summary>Chevron state: the expansion that applies to the current sidebar mode.</summary>
    public bool ShowsExpanded => ShowChildren;

    /// <summary>Collapsed sidebar or closed group: the header shows that one of its pages is open.</summary>
    public bool HighlightHeader => ContainsSelection && !ShowChildren;

    partial void OnIsExpandedChanged(bool value) => RefreshLayout();

    partial void OnIsRailExpandedChanged(bool value) => RefreshLayout();

    partial void OnContainsSelectionChanged(bool value) => OnPropertyChanged(nameof(HighlightHeader));

    public void RefreshLayout()
    {
        OnPropertyChanged(nameof(ShowChildren));
        OnPropertyChanged(nameof(ShowsExpanded));
        OnPropertyChanged(nameof(HighlightHeader));
    }

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Title));
        foreach (var item in Items)
        {
            item.RefreshText();
        }
    }
}
