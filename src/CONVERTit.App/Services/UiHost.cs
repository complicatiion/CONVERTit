using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CONVERTit.App.Localization;
using CONVERTit.App.Views;

namespace CONVERTit.App.Services;

/// <summary>Dialogs and pickers for view models (needs the main window).</summary>
public sealed class UiHost
{
    public Window? MainWindow { get; set; }

    private IStorageProvider? Storage => MainWindow is null ? null : TopLevel.GetTopLevel(MainWindow)?.StorageProvider;

    public async Task<List<string>> PickFilesAsync(string title, IReadOnlyCollection<string> extensions, bool multiple = true, string? startFolder = null)
    {
        var storage = Storage;
        if (storage is null)
        {
            return [];
        }

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple,
            SuggestedStartLocation = await StartAsync(storage, startFolder)
        };
        if (extensions.Count > 0)
        {
            options.FileTypeFilter =
            [
                new FilePickerFileType(Loc.T("FilterSupported")) { Patterns = extensions.Select(e => "*." + e).ToList() },
                new FilePickerFileType(Loc.T("FilterAll")) { Patterns = ["*.*"] }
            ];
        }

        var files = await storage.OpenFilePickerAsync(options);
        return files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
    }

    public async Task<string?> PickFolderAsync(string title, string? startFolder = null)
    {
        var storage = Storage;
        if (storage is null)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await StartAsync(storage, startFolder)
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false, string? confirmIcon = null)
    {
        if (MainWindow is null)
        {
            return false;
        }

        var dialog = new ConfirmWindow(title, message, confirmText, danger, confirmIcon);
        return await dialog.ShowDialog<bool>(MainWindow);
    }

    public async Task ShowTextAsync(string title, string text)
    {
        if (MainWindow is null)
        {
            return;
        }

        await new TextWindow(title, text).ShowDialog(MainWindow);
    }

    public async Task CopyTextAsync(string text)
    {
        var clipboard = MainWindow is null ? null : TopLevel.GetTopLevel(MainWindow)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private static async Task<IStorageFolder?> StartAsync(IStorageProvider storage, string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        try
        {
            return await storage.TryGetFolderFromPathAsync(folder);
        }
        catch
        {
            return null;
        }
    }
}
