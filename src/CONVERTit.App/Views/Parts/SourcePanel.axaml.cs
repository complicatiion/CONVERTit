using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CONVERTit.App.Services;
using CONVERTit.App.ViewModels;

namespace CONVERTit.App.Views.Parts;

/// <summary>File list with multi-selection and a right-click menu: the selected rows are passed to the view model.</summary>
public partial class SourcePanel : UserControl
{
    private SourceListViewModel? _model;

    public SourcePanel()
    {
        InitializeComponent();
        FileList.SelectionChanged += (_, _) =>
            _model?.SetSelection(FileList.SelectedItems?.OfType<SourceItem>() ?? Enumerable.Empty<SourceItem>());
        FileList.ContextRequested += OnContextRequested;
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
            {
                _model.SelectAllRequested -= SelectAll;
                _model.CopyToClipboard = null;
            }

            _model = DataContext as SourceListViewModel;
            if (_model is not null)
            {
                _model.SelectAllRequested += SelectAll;
                _model.CopyToClipboard = CopyAsync;
            }
        };
    }

    private void SelectAll() => FileList.SelectAll();

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <summary>
    /// Right-click on a row: a row outside the selection becomes the only selected row (like Explorer), then the
    /// menu offers open, folder, copy path, convert only these, send to another page and remove.
    /// </summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        var row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (row?.DataContext is SourceItem clicked && FileList.SelectedItems is { } selected && !selected.Contains(clicked))
        {
            selected.Clear();
            selected.Add(clicked);
        }

        var items = FileList.SelectedItems?.OfType<SourceItem>().ToList() ?? [];
        if (items.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu { ItemsSource = Build(_model.ContextActions(items)) };
        menu.Open(FileList);
        e.Handled = true;
    }

    private static List<Control> Build(IEnumerable<ListAction?> actions)
    {
        var controls = new List<Control>();
        foreach (var action in actions)
        {
            if (action is null)
            {
                controls.Add(new Separator());
                continue;
            }

            var item = new MenuItem
            {
                Header = action.Text,
                Icon = new PathIcon { Data = Res.Icon(action.IconKey), Width = 16, Height = 16 }
            };
            if (action.Danger)
            {
                item.Classes.Add("danger");
            }

            if (action.Children is { Count: > 0 } children)
            {
                item.ItemsSource = Build(children);
            }
            else if (action.Run is { } run)
            {
                item.Click += (_, _) => run();
            }

            controls.Add(item);
        }

        return controls;
    }
}
