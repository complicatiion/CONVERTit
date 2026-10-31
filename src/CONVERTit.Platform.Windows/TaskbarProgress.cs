using System.Runtime.InteropServices;

namespace CONVERTit.Platform.Windows;

public enum TaskbarState
{
    None = 0,
    Indeterminate = 0x1,
    Normal = 0x2,
    Error = 0x4,
    Paused = 0x8
}

/// <summary>Progress bar in the taskbar button (ITaskbarList3). Call from the UI thread.</summary>
public sealed class TaskbarProgress
{
    private readonly ITaskbarList3? _taskbar;

    public TaskbarProgress()
    {
        try
        {
            _taskbar = (ITaskbarList3)new TaskbarInstance();
            _taskbar.HrInit();
        }
        catch
        {
            _taskbar = null;
        }
    }

    public void Set(IntPtr hwnd, TaskbarState state, double fraction = 0)
    {
        if (_taskbar is null || hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _taskbar.SetProgressState(hwnd, state);
            if (state is TaskbarState.Normal or TaskbarState.Error or TaskbarState.Paused)
            {
                _taskbar.SetProgressValue(hwnd, (ulong)(Math.Clamp(fraction, 0, 1) * 1000), 1000);
            }
        }
        catch
        {
            // Explorer restarted; nothing to do.
        }
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        [PreserveSig] void HrInit();
        [PreserveSig] void AddTab(IntPtr hwnd);
        [PreserveSig] void DeleteTab(IntPtr hwnd);
        [PreserveSig] void ActivateTab(IntPtr hwnd);
        [PreserveSig] void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        [PreserveSig] void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        // ITaskbarList3 (only the progress methods are used; the rest of the vtable follows them)
        [PreserveSig] void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        [PreserveSig] void SetProgressState(IntPtr hwnd, TaskbarState state);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarInstance
    {
    }
}
