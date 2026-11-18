using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CONVERTit.Platform.Windows;

/// <summary>Power source of the PC (GetSystemPowerStatus).</summary>
public sealed record PowerState(bool HasBattery, bool OnBattery, int BatteryPercent, bool EnergySaver);

/// <summary>CPU name and topology (Intel hybrid CPUs: efficiency cores).</summary>
public sealed record CpuDetails(string Name, int LogicalCores, int PhysicalCores, int EfficiencyCores);

/// <summary>Graphics adapter from DXGI (VendorId 0x10DE NVIDIA, 0x1002 AMD, 0x8086 Intel).</summary>
public sealed record GraphicsAdapter(string Name, uint VendorId, long DedicatedMemory);

/// <summary>
/// Fast system detection for the performance profiles: only Win32/DXGI calls, no WMI. Every call returns a
/// neutral value when something is not available.
/// </summary>
public static class SystemDetect
{
    // ---------------------------------------------------------------- Power

    public static PowerState Power()
    {
        try
        {
            if (!GetSystemPowerStatus(out var s))
            {
                return new PowerState(false, false, -1, false);
            }

            // BatteryFlag 128 = no system battery, 255 = unknown.
            var hasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255;
            var onBattery = hasBattery && s.ACLineStatus == 0;
            var percent = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : -1;
            // SystemStatusFlag 1 = battery saver / energy saver is on.
            return new PowerState(hasBattery, onBattery, percent, s.SystemStatusFlag == 1);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return new PowerState(false, false, -1, false);
        }
    }

    // ---------------------------------------------------------------- CPU & memory

    public static CpuDetails Cpu()
    {
        var name = "";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            name = (key?.GetValue("ProcessorNameString") as string ?? "").Trim();
        }
        catch
        {
            // Name is cosmetic.
        }

        var logical = Environment.ProcessorCount;
        var physical = 0;
        var efficiency = 0;
        try
        {
            uint length = 0;
            GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
            if (length > 0)
            {
                var buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    if (GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
                    {
                        var classes = new List<byte>();
                        var offset = 0;
                        while (offset < length)
                        {
                            var size = Marshal.ReadInt32(buffer, offset + 4);
                            // PROCESSOR_RELATIONSHIP: Flags (offset 8), EfficiencyClass (offset 9).
                            classes.Add(Marshal.ReadByte(buffer, offset + 9));
                            offset += Math.Max(size, 1);
                        }

                        physical = classes.Count;
                        var max = classes.Count > 0 ? classes.Max() : (byte)0;
                        // Hybrid CPUs: performance cores have the higher class, efficiency cores the lower.
                        efficiency = max > 0 ? classes.Count(c => c < max) : 0;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Older systems: logical cores only.
        }

        return new CpuDetails(name, logical, physical, efficiency);
    }

    public static long TotalMemory()
    {
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? (long)status.TotalPhys : 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return 0;
        }
    }

    // ---------------------------------------------------------------- Graphics adapters (DXGI)

    private static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint DxgiAdapterFlagSoftware = 2;

    /// <summary>Hardware adapters (the Microsoft Basic Render Driver is skipped).</summary>
    public static unsafe IReadOnlyList<GraphicsAdapter> Gpus()
    {
        var result = new List<GraphicsAdapter>();
        var factory = IntPtr.Zero;
        try
        {
            var iid = IidDxgiFactory1;
            if (CreateDXGIFactory1(ref iid, out factory) < 0 || factory == IntPtr.Zero)
            {
                return result;
            }

            // IDXGIFactory1::EnumAdapters1 = vtable slot 12.
            var factoryVtbl = *(IntPtr**)factory;
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)factoryVtbl[12];
            for (uint i = 0; i < 16; i++)
            {
                IntPtr adapter;
                var hr = enumAdapters1(factory, i, &adapter);
                if (hr == DxgiErrorNotFound || hr < 0 || adapter == IntPtr.Zero)
                {
                    break;
                }

                try
                {
                    // IDXGIAdapter1::GetDesc1 = vtable slot 10.
                    var adapterVtbl = *(IntPtr**)adapter;
                    var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, AdapterDesc1*, int>)adapterVtbl[10];
                    AdapterDesc1 desc;
                    if (getDesc1(adapter, &desc) >= 0 && (desc.Flags & DxgiAdapterFlagSoftware) == 0)
                    {
                        var name = new string(desc.Description).TrimEnd('\0').Trim();
                        var vendor = desc.VendorId;
                        var memory = (long)desc.DedicatedVideoMemory;
                        // The same GPU can be listed once per output.
                        if (!result.Exists(r => r.Name == name && r.VendorId == vendor))
                        {
                            result.Add(new GraphicsAdapter(name, vendor, memory));
                        }
                    }
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or SEHException)
        {
            // No DXGI: no adapters listed.
        }
        finally
        {
            if (factory != IntPtr.Zero)
            {
                Marshal.Release(factory);
            }
        }

        return result;
    }

    // ---------------------------------------------------------------- Native

    private const int RelationProcessorCore = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
}

/// <summary>
/// EcoQoS (Windows 11, on Windows 10 1709+ "power throttling"): the process prefers efficiency cores and lower
/// clocks. Used by the Eco profile for FFmpeg processes.
/// </summary>
public static class ProcessPower
{
    private const int ProcessPowerThrottling = 4;
    private const uint ExecutionSpeed = 0x1;

    public static void SetEcoQos(Process process, bool eco)
    {
        try
        {
            var state = new PowerThrottlingState
            {
                Version = 1,
                ControlMask = ExecutionSpeed,
                StateMask = eco ? ExecutionSpeed : 0
            };
            SetProcessInformation(process.Handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>());
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Older Windows or the process has already exited.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState information, uint size);
}

/// <summary>Start with Windows for the current user (HKCU\…\Run); the installer uses the same entry.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(name) is string { Length: > 0 };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Writes or removes the entry: "exe" --tray.</summary>
    public static void Set(string name, bool enabled, string executable, string arguments)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            key.SetValue(name, $"\"{executable}\" {arguments}".TrimEnd());
        }
        else if (key.GetValue(name) is not null)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
