using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CONVERTit.Platform.Windows;

/// <summary>One entry of the "CONVERTit" submenu: action id passed to the app and its display text.</summary>
public sealed record ExplorerVerb(string Id, string Label);

/// <summary>Submenu for one group of file types (video, audio or images).</summary>
public sealed record ExplorerMenuGroup(IReadOnlyCollection<string> Extensions, IReadOnlyList<ExplorerVerb> Verbs);

/// <summary>
/// Classic Explorer context menu (Windows 10 menu; on Windows 11 under "Show more options"). Static cascading verbs
/// below HKCU\Software\Classes\SystemFileAssociations\.ext\shell\CONVERTit – per user, no administrator rights,
/// no shell extension DLL. Each verb starts CONVERTit with "--action id file"; several selected files are collected
/// by the running instance.
/// </summary>
public static class ExplorerMenu
{
    private const string Root = @"Software\Classes\SystemFileAssociations\.";
    private const string KeyName = "CONVERTit";

    public static void Register(string executablePath, string menuLabel, IEnumerable<ExplorerMenuGroup> groups)
    {
        foreach (var group in groups)
        {
            foreach (var ext in group.Extensions)
            {
                var path = $@"{Root}{ext}\shell\{KeyName}";
                Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
                using (var key = Registry.CurrentUser.CreateSubKey(path))
                {
                    key.SetValue("MUIVerb", menuLabel);
                    key.SetValue("Icon", $"\"{executablePath}\",0");
                    // Empty SubCommands = the entries below "shell" form the submenu.
                    key.SetValue("SubCommands", "");
                    key.SetValue("MultiSelectModel", "Player");
                }

                for (var i = 0; i < group.Verbs.Count; i++)
                {
                    var verb = group.Verbs[i];
                    using var sub = Registry.CurrentUser.CreateSubKey($@"{path}\shell\{i:00}{verb.Id}");
                    sub.SetValue("MUIVerb", verb.Label);
                    sub.SetValue("MultiSelectModel", "Player");
                    using var command = sub.CreateSubKey("command");
                    command.SetValue("", $"\"{executablePath}\" --action {verb.Id} \"%1\"");
                }
            }
        }

        NotifyShell();
    }

    public static void Unregister(IEnumerable<string> extensions)
    {
        foreach (var ext in extensions)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"{Root}{ext}\shell\{KeyName}", throwOnMissingSubKey: false);
            }
            catch
            {
                // Key in use or no access: skip.
            }
        }

        NotifyShell();
    }

    public static bool IsRegistered(string extension)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{Root}{extension}\shell\{KeyName}");
        return key is not null;
    }

    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
        }
        catch
        {
            // Explorer picks the change up later.
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}

/// <summary>Language hints of the system for the first start.</summary>
public static class SystemLanguage
{
    /// <summary>Primary language id of the active keyboard layout (0x07 = German), 0 when unknown.</summary>
    public static int KeyboardPrimaryLanguage()
    {
        try
        {
            var layout = GetKeyboardLayout(0);
            return (int)((long)layout & 0x3FF);
        }
        catch
        {
            return 0;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);
}
