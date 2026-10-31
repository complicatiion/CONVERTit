using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace CONVERTit.Platform.Windows;

/// <summary>Result of a toast attempt; <see cref="Error"/> explains why nothing was shown.</summary>
public readonly record struct ToastResult(bool Shown, string? Error)
{
    public static ToastResult Ok => new(true, null);
}

/// <summary>
/// Windows toast notifications for an unpackaged desktop app. The AppUserModelID is registered under
/// HKCU\Software\Classes\AppUserModelId, the "Open folder" button uses the per-user protocol convertit://
/// (a new CONVERTit process opens Explorer and exits immediately). Nothing needs administrator rights.
/// The toast is shown directly through the WinRT API (combase.dll activation and raw COM calls): no PowerShell,
/// no Windows SDK projection, so the app keeps its plain net10.0-windows target.
/// </summary>
public static class Notifications
{
    public const string AppId = "sksdesign.CONVERTit";
    public const string Protocol = "convertit";

    private const string AppIdKey = @"Software\Classes\AppUserModelId\" + AppId;
    private const string ProtocolKey = @"Software\Classes\" + Protocol;

    /// <summary>Registers display name, icon and the protocol handler for the current user (idempotent).</summary>
    public static void Register(string displayName, string iconPngPath, string executablePath)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(AppIdKey))
            {
                key.SetValue("DisplayName", displayName);
                key.SetValue("ShowInSettings", 1, RegistryValueKind.DWord);
                if (File.Exists(iconPngPath))
                {
                    key.SetValue("IconUri", iconPngPath);
                }
            }

            using (var key = Registry.CurrentUser.CreateSubKey(ProtocolKey))
            {
                key.SetValue("", "URL:" + displayName);
                key.SetValue("URL Protocol", "");
            }

            using (var key = Registry.CurrentUser.CreateSubKey(ProtocolKey + @"\shell\open\command"))
            {
                key.SetValue("", $"\"{executablePath}\" \"%1\"");
            }
        }
        catch
        {
            // Registry not writable: in-app notifications still work.
        }
    }

    /// <summary>Removes the registration (Settings › Windows integration).</summary>
    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(AppIdKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(ProtocolKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // Nothing registered.
        }
    }

    /// <summary>Shows a toast; the button opens <paramref name="folder"/> in Explorer.</summary>
    public static ToastResult Show(string title, string text, string? folder, string openFolderLabel)
    {
        var launch = string.IsNullOrEmpty(folder) ? "" : OpenFolderUri(folder);
        var actions = string.IsNullOrEmpty(folder)
            ? ""
            : $"<actions><action content=\"{SecurityElement.Escape(openFolderLabel)}\" activationType=\"protocol\" arguments=\"{SecurityElement.Escape(launch)}\"/></actions>";
        var launchAttribute = string.IsNullOrEmpty(folder) ? "" : $" activationType=\"protocol\" launch=\"{SecurityElement.Escape(launch)}\"";
        var xml =
            $"<toast{launchAttribute}><visual><binding template=\"ToastGeneric\">" +
            $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(text)}</text>" +
            $"</binding></visual>{actions}</toast>";

        // WinRT objects are created on a short-lived MTA thread (independent of the UI thread's apartment).
        ToastResult result = new(false, "Timeout");
        var thread = new Thread(() => result = ShowCore(xml)) { IsBackground = true, Name = "CONVERTit.Toast" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return thread.Join(TimeSpan.FromSeconds(5)) ? result : new ToastResult(false, "Timeout");
    }

    private static ToastResult ShowCore(string xml)
    {
        try
        {
            var init = WinRt.RoInitialize(1);
            if (init < 0 && init != WinRt.RpcEChangedMode)
            {
                return Fail("RoInitialize", init);
            }

            return WinRt.ShowToast(AppId, xml);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new ToastResult(false, "WinRT not available (Windows 10 or newer required)");
        }
        catch (Exception ex)
        {
            return new ToastResult(false, ex.Message);
        }
    }

    internal static ToastResult Fail(string step, int hr) => new(false, $"{step} failed (0x{hr:X8})");

    /// <summary>convertit://open-folder?path=… (path URL-encoded).</summary>
    public static string OpenFolderUri(string folder) =>
        $"{Protocol}://open-folder?path={Uri.EscapeDataString(folder)}";

    /// <summary>Handles a protocol activation; returns true when the argument was a convertit:// URI.</summary>
    public static bool HandleActivation(string argument)
    {
        if (!argument.StartsWith(Protocol + "://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var uri = new Uri(argument);
            var query = uri.Query.TrimStart('?');
            foreach (var part in query.Split('&'))
            {
                var pair = part.Split('=', 2);
                if (pair.Length == 2 && pair[0] == "path")
                {
                    var path = Uri.UnescapeDataString(pair[1]);
                    // Only existing local folders/files are shown (no URLs, no network paths, nothing is executed).
                    var local = Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal);
                    if (local && (Directory.Exists(path) || File.Exists(path)))
                    {
                        Shell.SelectInExplorer(path);
                    }
                }
            }
        }
        catch
        {
            // Invalid URI: ignore.
        }

        return true;
    }
}

/// <summary>
/// Minimal WinRT access via combase.dll: activation factories and vtable calls. Interface layouts (IIDs and
/// method order) follow windows.ui.notifications.idl and windows.data.xml.dom.idl. Slots 0–2 are IUnknown,
/// 3–5 IInspectable, the interface methods start at slot 6.
/// </summary>
internal static unsafe class WinRt
{
    public const int RpcEChangedMode = unchecked((int)0x80010106);

    private static readonly Guid IidToastManagerStatics = new("50ac103f-d235-4598-bbef-98fe4d1a3ad4");
    private static readonly Guid IidToastNotificationFactory = new("04124b20-82c6-4229-b109-fd9ed4662b53");
    private static readonly Guid IidXmlDocumentIo = new("6cd0e74e-ee65-4489-9ebf-ca43e87ba637");

    /// <summary>ToastTemplateType.ToastText02: any template works, its content is replaced by LoadXml.</summary>
    private const int ToastText02 = 5;

    public static ToastResult ShowToast(string appId, string xml)
    {
        IntPtr statics = 0, notifier = 0, document = 0, documentIo = 0, factory = 0, toast = 0;
        try
        {
            var hr = GetFactory("Windows.UI.Notifications.ToastNotificationManager", IidToastManagerStatics, out statics);
            if (hr < 0) return Notifications.Fail("ToastNotificationManager", hr);

            // IToastNotificationManagerStatics: [6] CreateToastNotifier, [7] CreateToastNotifierWithId, [8] GetTemplateContent
            using (var id = new HString(appId))
            {
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(statics, 7))(statics, id.Handle, &notifier);
            }

            if (hr < 0) return Notifications.Fail("CreateToastNotifier", hr);

            // IToastNotifier: [8] get_Setting – 0 = enabled; otherwise Windows would drop the toast silently.
            int setting;
            if (((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(notifier, 8))(notifier, &setting) >= 0 && setting != 0)
            {
                return new ToastResult(false, setting switch
                {
                    1 => "Notifications are turned off for CONVERTit (Windows Settings › System › Notifications)",
                    2 => "Notifications are turned off for this user (Windows Settings › System › Notifications)",
                    3 => "Notifications are turned off by group policy",
                    _ => $"Notifications are not available (setting {setting})"
                });
            }

            // The template call returns an XmlDocument (its default interface) without needing another IID.
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Slot(statics, 8))(statics, ToastText02, &document);
            if (hr < 0) return Notifications.Fail("GetTemplateContent", hr);

            hr = Marshal.QueryInterface(document, in IidXmlDocumentIo, out documentIo);
            if (hr < 0) return Notifications.Fail("IXmlDocumentIO", hr);

            // IXmlDocumentIO: [6] LoadXml
            using (var content = new HString(xml))
            {
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(documentIo, 6))(documentIo, content.Handle);
            }

            if (hr < 0) return Notifications.Fail("LoadXml", hr);

            hr = GetFactory("Windows.UI.Notifications.ToastNotification", IidToastNotificationFactory, out factory);
            if (hr < 0) return Notifications.Fail("ToastNotification", hr);

            // IToastNotificationFactory: [6] CreateToastNotification(XmlDocument)
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(factory, 6))(factory, document, &toast);
            if (hr < 0) return Notifications.Fail("CreateToastNotification", hr);

            // IToastNotifier: [6] Show
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(notifier, 6))(notifier, toast);
            return hr < 0 ? Notifications.Fail("Show", hr) : ToastResult.Ok;
        }
        finally
        {
            foreach (var p in new[] { toast, factory, documentIo, document, notifier, statics })
            {
                if (p != 0)
                {
                    Marshal.Release(p);
                }
            }
        }
    }

    private static IntPtr Slot(IntPtr instance, int index) => (*(IntPtr**)instance)[index];

    private static int GetFactory(string className, Guid iid, out IntPtr factory)
    {
        using var name = new HString(className);
        return RoGetActivationFactory(name.Handle, in iid, out factory);
    }

    [DllImport("combase.dll")]
    public static extern int RoInitialize(int initType);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    /// <summary>HSTRING with deterministic release.</summary>
    private readonly struct HString : IDisposable
    {
        public HString(string value)
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(value, value.Length, out var handle));
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public void Dispose()
        {
            if (Handle != 0)
            {
                WindowsDeleteString(Handle);
            }
        }
    }
}
