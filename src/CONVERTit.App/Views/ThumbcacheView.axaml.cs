using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CONVERTit.App.Localization;
using CONVERTit.App.ViewModels;

namespace CONVERTit.App.Views;

/// <summary>
/// Thumbnail grid: Explorer-like multi-selection, double-click/Enter/Space opens the viewer, right-click menu,
/// M marks, Ctrl+C copies the image. In the viewer: ←/→, +/−, 0 = fit, 1 = 100 %, mouse wheel = zoom, Esc closes.
/// </summary>
public partial class ThumbcacheView : UserControl
{
    private ThumbcacheViewModel? _vm;

    public ThumbcacheView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ThumbcacheViewModel);
        ThumbGrid.SelectionChanged += (_, _) => _vm?.SetSelection(ThumbGrid.SelectedItems?.OfType<CacheThumb>() ?? []);
        ThumbGrid.DoubleTapped += (_, e) =>
        {
            if (ItemAt(e.Source) is { } item)
            {
                _vm?.OpenViewerCommand.Execute(item);
            }
        };
        ThumbGrid.ContextRequested += OnContext;
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        ViewerStage.PointerWheelChanged += (_, e) =>
        {
            if (_vm is null)
            {
                return;
            }

            (e.Delta.Y > 0 ? _vm.ZoomInCommand : _vm.ZoomOutCommand).Execute(null);
            e.Handled = true;
        };
    }

    private void Attach(ThumbcacheViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.SelectAllRequested -= OnSelectAll;
            _vm.PropertyChanged -= OnVmChanged;
        }

        _vm = vm;
        if (_vm is not null)
        {
            _vm.SelectAllRequested += OnSelectAll;
            _vm.PropertyChanged += OnVmChanged;
        }
    }

    private void OnSelectAll(bool select)
    {
        if (select)
        {
            ThumbGrid.SelectAll();
        }
        else
        {
            ThumbGrid.UnselectAll();
        }
    }

    /// <summary>The viewer takes the keyboard while it is open, the grid gets it back afterwards.</summary>
    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ThumbcacheViewModel.IsViewerOpen) || _vm is null)
        {
            return;
        }

        if (_vm.IsViewerOpen)
        {
            Viewer.Focus();
        }
        else
        {
            ThumbGrid.Focus();
        }
    }

    private static CacheThumb? ItemAt(object? source) =>
        (source as Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as CacheThumb;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.Source is TextBox)
        {
            return;
        }

        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        var none = e.KeyModifiers == KeyModifiers.None;
        if (_vm.IsViewerOpen)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    _vm.CloseViewerCommand.Execute(null);
                    break;
                case Key.Left or Key.Up or Key.PageUp:
                    _vm.ViewerPreviousCommand.Execute(null);
                    break;
                case Key.Right or Key.Down or Key.PageDown or Key.Space:
                    _vm.ViewerNextCommand.Execute(null);
                    break;
                case Key.OemPlus or Key.Add:
                    _vm.ZoomInCommand.Execute(null);
                    break;
                case Key.OemMinus or Key.Subtract:
                    _vm.ZoomOutCommand.Execute(null);
                    break;
                case Key.D0 or Key.NumPad0:
                    _vm.ZoomFitCommand.Execute(null);
                    break;
                case Key.D1 or Key.NumPad1:
                    _vm.ZoomActualCommand.Execute(null);
                    break;
                case Key.M when none:
                    _vm.ToggleMarkCommand.Execute(_vm.ViewerItem);
                    break;
                case Key.C when ctrl:
                    _vm.CopyImageCommand.Execute(_vm.ViewerItem);
                    break;
                default:
                    return;
            }

            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter or Key.Space when none && _vm.HasSelection:
                _vm.OpenViewerCommand.Execute(_vm.Selection[^1]);
                break;
            case Key.M when none && _vm.HasSelection:
                _vm.ToggleMarkCommand.Execute(null);
                break;
            case Key.C when ctrl && _vm.HasSelection:
                _vm.CopyImageCommand.Execute(_vm.Selection[^1]);
                break;
            case Key.A when ctrl && _vm.HasItems:
                ThumbGrid.SelectAll();
                break;
            case Key.Escape when _vm.HasSelection:
                ThumbGrid.UnselectAll();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Right-click: the clicked tile joins the selection (or becomes it when it was not selected).</summary>
    private void OnContext(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm is null || ItemAt(e.Source) is not { } item)
        {
            return;
        }

        if (ThumbGrid.SelectedItems is { } selected && !selected.Contains(item))
        {
            ThumbGrid.SelectedItem = item;
        }

        var many = _vm.SelectedCount > 1;
        var marked = _vm.Selection.Count > 0 && _vm.Selection.All(i => i.IsMarked);
        var items = new List<Control>
        {
            Item(Loc.T("CtxView"), "IconEye", () => _vm.OpenViewerCommand.Execute(item)),
            new Separator(),
            Item(Loc.T(many ? "CtxExportSelected" : "CtxExport"), "IconExport", () => _vm.ExportSelectedCommand.Execute(null)),
            Item(Loc.T("BtnCopyImage"), "IconContentCopy", () => _vm.CopyImageCommand.Execute(item)),
            Item(Loc.T(item.Data.Entry.Name is null ? "CtxCopyCacheId" : "CtxCopyName"), "IconContentCopy", () => _vm.CopyIdCommand.Execute(item)),
            Item(Loc.T("CtxSendToUpscale"), "IconImageFilterHdr", () => _vm.SendToUpscaleCommand.Execute(item)),
            new Separator(),
            Item(Loc.T(marked ? "CtxUnmark" : "CtxMark"), marked ? "IconStarOutline" : "IconStar", () => _vm.ToggleMarkCommand.Execute(null)),
            Item(Loc.T("BtnSelectAll"), "IconSelectAll", () => ThumbGrid.SelectAll()),
            Item(Loc.T("CtxDeselectAll"), "IconClose", () => ThumbGrid.UnselectAll())
        };
        if (_vm.HasMarks)
        {
            items.Add(Item(Loc.T("CtxClearMarks"), "IconStarOutline", () => _vm.ClearMarksCommand.Execute(null)));
        }

        new ContextMenu { ItemsSource = items }.Open(ThumbGrid);
        e.Handled = true;
    }

    private static MenuItem Item(string text, string icon, Action run)
    {
        var item = new MenuItem { Header = text, Icon = new PathIcon { Data = Res.Icon(icon), Width = 16, Height = 16 } };
        item.Click += (_, _) => run();
        return item;
    }
}
