using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.Core.Files;
using CONVERTit.Core.Options;

namespace CONVERTit.App.ViewModels;

/// <summary>Output card: destination, folder structure, suffix, conflicts, dates.</summary>
public sealed partial class OutputViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public OutputViewModel(MainViewModel main)
    {
        _main = main;
        Options.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OutputOptions.Destination))
            {
                OnPropertyChanged(nameof(IsCustom));
                OnPropertyChanged(nameof(IsSubfolder));
            }
        };
    }

    public OutputOptions Options { get; } = new();

    public OutputDestination[] Destinations => Choices.Destinations;

    public ConflictPolicy[] Conflicts => Choices.Conflicts;

    public bool IsCustom => Options.Destination == OutputDestination.Custom;

    public bool IsSubfolder => Options.Destination == OutputDestination.Subfolder;

    /// <summary>Folders a folder batch must not scan (its own output).</summary>
    public IEnumerable<string> ExcludedFolders(string root) => new OutputPathResolver(Options).ExcludedFolders(root);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await _main.Host.PickFolderAsync(Loc.T("PickOutputFolder"), Options.CustomFolder);
        if (!string.IsNullOrEmpty(folder))
        {
            Options.CustomFolder = folder;
            Options.Destination = OutputDestination.Custom;
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}
