using System.ComponentModel;

namespace CONVERTit.App.Localization;

/// <summary>
/// Observable language version: bound next to every enum label (MultiBinding), so all choice lists
/// relabel themselves when the language changes without replacing their items (which would reset selections).
/// </summary>
public sealed class LocState : INotifyPropertyChanged
{
    public static LocState Instance { get; } = new();

    public int Version { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Bump()
    {
        Version++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version)));
    }
}
