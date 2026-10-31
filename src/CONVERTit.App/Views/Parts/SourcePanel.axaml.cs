using Avalonia.Controls;
using CONVERTit.App.ViewModels;

namespace CONVERTit.App.Views.Parts;

/// <summary>File list with multi-selection: the selected rows are passed to the view model.</summary>
public partial class SourcePanel : UserControl
{
    private SourceListViewModel? _model;

    public SourcePanel()
    {
        InitializeComponent();
        FileList.SelectionChanged += (_, _) =>
            _model?.SetSelection(FileList.SelectedItems?.OfType<SourceItem>() ?? Enumerable.Empty<SourceItem>());
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
            {
                _model.SelectAllRequested -= SelectAll;
            }

            _model = DataContext as SourceListViewModel;
            if (_model is not null)
            {
                _model.SelectAllRequested += SelectAll;
            }
        };
    }

    private void SelectAll() => FileList.SelectAll();
}
