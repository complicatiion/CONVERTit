using Avalonia;
using Avalonia.Controls;
using CONVERTit.App.ViewModels;
using CONVERTit.App.Views;

namespace CONVERTit.App.Controls;

/// <summary>
/// Hosts all page views at once and only toggles their visibility. Views stay in the visual tree, so switching
/// pages neither re-creates nor re-styles them (and the player keeps its state). Each view is created on its first visit.
/// </summary>
public sealed class PageHost : Grid
{
    public static readonly StyledProperty<PageViewModel?> PageProperty =
        AvaloniaProperty.Register<PageHost, PageViewModel?>(nameof(Page));

    private readonly Dictionary<PageViewModel, Control> _views = new();

    static PageHost()
    {
        PageProperty.Changed.AddClassHandler<PageHost>((host, _) => host.ShowPage());
    }

    public PageViewModel? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    private void ShowPage()
    {
        var page = Page;
        if (page is not null && !_views.ContainsKey(page))
        {
            var view = Create(page);
            view.DataContext = page;
            _views[page] = view;
            Children.Add(view);
        }

        foreach (var (model, view) in _views)
        {
            var visible = ReferenceEquals(model, page);
            if (visible && !view.IsVisible)
            {
                view.IsVisible = true;
                Motion.FadeIn(view, offsetY: 12);
            }
            else
            {
                view.IsVisible = visible;
            }
        }
    }

    private static Control Create(PageViewModel page) => page switch
    {
        QuickConvertViewModel => new QuickConvertView(),
        CompressViewModel => new CompressView(),
        GameTextureViewModel => new GameTextureView(),
        GameAudioViewModel => new GameAudioView(),
        TagEditorViewModel => new TagEditorView(),
        VideoConvertViewModel => new VideoConvertView(),
        TrimViewModel => new TrimView(),
        AnimationViewModel => new AnimationView(),
        RemuxViewModel => new RemuxView(),
        ExtractAudioViewModel => new ExtractAudioView(),
        AudioConvertViewModel => new AudioConvertView(),
        ImageConvertViewModel => new ImageConvertView(),
        IconViewModel => new IconView(),
        QueueViewModel => new QueueView(),
        DependenciesViewModel => new DependenciesView(),
        SettingsViewModel => new SettingsView(),
        AboutViewModel => new AboutView(),
        _ => new TextBlock { Text = page.GetType().Name }
    };
}
