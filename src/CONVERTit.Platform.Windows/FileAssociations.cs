using System.Diagnostics;
using Microsoft.Win32;

namespace CONVERTit.Platform.Windows;

/// <summary>A program type for file associations (video, audio, image) with its extensions (without dot).</summary>
public sealed record AssociationGroup(string ProgId, string FriendlyName, IReadOnlyCollection<string> Extensions);

/// <summary>
/// File associations per user (HKCU, no administrator rights). Since Windows 10 a program may not make itself the
/// default app: CONVERTit registers its program types, adds itself to "Open with" (OpenWithProgids) and to
/// Settings › Default apps (RegisteredApplications). Choosing it as default is confirmed by the user in Windows.
/// Existing defaults of other programs are never changed.
/// </summary>
public static class FileAssociations
{
    private const string Classes = @"Software\Classes";
    private const string AppName = "CONVERTit";
    private const string CapabilitiesPath = @"Software\CONVERTit\Capabilities";

    public static void Register(string executablePath, string description, IEnumerable<AssociationGroup> groups)
    {
        Unregister(notify: false);
        using var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesPath);
        capabilities.SetValue("ApplicationName", AppName);
        capabilities.SetValue("ApplicationDescription", description);
        capabilities.SetValue("ApplicationIcon", $"\"{executablePath}\",0");
        using var fileAssociations = capabilities.CreateSubKey("FileAssociations");

        foreach (var group in groups)
        {
            using (var progId = Registry.CurrentUser.CreateSubKey($@"{Classes}\{group.ProgId}"))
            {
                progId.SetValue("", group.FriendlyName);
                progId.SetValue("FriendlyTypeName", group.FriendlyName);
                using var icon = progId.CreateSubKey("DefaultIcon");
                icon.SetValue("", $"\"{executablePath}\",0");
                using var command = progId.CreateSubKey(@"shell\open\command");
                command.SetValue("", $"\"{executablePath}\" \"%1\"");
            }

            foreach (var ext in group.Extensions)
            {
                using var openWith = Registry.CurrentUser.CreateSubKey($@"{Classes}\.{ext}\OpenWithProgids");
                openWith.SetValue(group.ProgId, Array.Empty<byte>(), RegistryValueKind.None);
                fileAssociations.SetValue("." + ext, group.ProgId);
            }
        }

        using var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications");
        registered.SetValue(AppName, CapabilitiesPath);
        NotifyShell();
    }

    /// <summary>Removes everything Register wrote (program types, "Open with" entries, default apps entry).</summary>
    public static void Unregister(bool notify = true)
    {
        try
        {
            using (var capabilities = Registry.CurrentUser.OpenSubKey($@"{CapabilitiesPath}\FileAssociations"))
            {
                if (capabilities is not null)
                {
                    foreach (var ext in capabilities.GetValueNames())
                    {
                        var progId = capabilities.GetValue(ext) as string;
                        using var openWith = Registry.CurrentUser.OpenSubKey($@"{Classes}\{ext}\OpenWithProgids", writable: true);
                        if (progId is not null && openWith?.GetValue(progId) is not null)
                        {
                            openWith.DeleteValue(progId, throwOnMissingValue: false);
                        }
                    }
                }
            }

            foreach (var progId in new[] { "CONVERTit.Video", "CONVERTit.Audio", "CONVERTit.Image" })
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\{progId}", throwOnMissingSubKey: false);
            }

            Registry.CurrentUser.DeleteSubKeyTree(@"Software\CONVERTit", throwOnMissingSubKey: false);
            using var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true);
            registered?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch
        {
            // No access: leave what is there.
        }

        if (notify)
        {
            NotifyShell();
        }
    }

    /// <summary>Number of file types currently registered by CONVERTit.</summary>
    public static int RegisteredCount()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{CapabilitiesPath}\FileAssociations");
        return key?.ValueCount ?? 0;
    }

    /// <summary>Opens Settings › Default apps on the CONVERTit page (Windows 11; Windows 10 shows the overview).</summary>
    public static void OpenDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppUser={AppName}") { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
    }

    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Explorer picks the change up later.
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
