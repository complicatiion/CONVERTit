using Avalonia.Threading;
using CONVERTit.Core.Machine;

namespace CONVERTit.App.Services;

/// <summary>
/// System detection for the performance profiles: CPU, memory and graphics adapters once (in the background),
/// power source and energy saver every 20 seconds. <see cref="Changed"/> is raised on the UI thread.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;

    public SystemMonitor()
    {
        Info = MachineInfo.Unknown;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => RefreshPower();
    }

    public MachineInfo Info { get; private set; }

    /// <summary>Hardware was detected (CPU, memory, GPUs).</summary>
    public bool IsDetected { get; private set; }

    public event Action? Changed;

    /// <summary>Detects the hardware on a worker thread, then watches the power source.</summary>
    public void Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RefreshPower();
        _ = Task.Run(() =>
        {
            try
            {
                var cpu = Platform.Windows.SystemDetect.Cpu();
                var memory = Platform.Windows.SystemDetect.TotalMemory();
                var gpus = Platform.Windows.SystemDetect.Gpus().Select(ToGpu).ToList();
                Dispatcher.UIThread.Post(() =>
                {
                    Info = Info with
                    {
                        CpuName = cpu.Name,
                        LogicalCores = cpu.LogicalCores,
                        PhysicalCores = cpu.PhysicalCores,
                        EfficiencyCores = cpu.EfficiencyCores,
                        MemoryBytes = memory,
                        Gpus = gpus
                    };
                    IsDetected = true;
                    Changed?.Invoke();
                });
            }
            catch
            {
                // Detection is best effort: the profiles then work with the core count only.
            }
        });
        _timer.Start();
    }

    private void RefreshPower()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var power = Platform.Windows.SystemDetect.Power();
        // Battery level only matters in 5 % steps (fewer updates).
        var percent = power.BatteryPercent < 0 ? -1 : power.BatteryPercent / 5 * 5;
        if (power.HasBattery == Info.HasBattery && power.OnBattery == Info.OnBattery && power.EnergySaver == Info.EnergySaver
            && percent == Info.BatteryPercent)
        {
            return;
        }

        Info = Info with { HasBattery = power.HasBattery, OnBattery = power.OnBattery, EnergySaver = power.EnergySaver, BatteryPercent = percent };
        Changed?.Invoke();
    }

    /// <summary>Integrated: Intel/AMD with little dedicated memory (APUs and iGPUs report a small reserved area).</summary>
    private static GpuInfo ToGpu(Platform.Windows.GraphicsAdapter adapter)
    {
        var vendor = adapter.VendorId switch
        {
            0x10DE => GpuVendor.Nvidia,
            0x1002 or 0x1022 => GpuVendor.Amd,
            0x8086 => GpuVendor.Intel,
            _ => GpuVendor.Other
        };
        var integrated = vendor != GpuVendor.Nvidia && adapter.DedicatedMemory < 2L * 1024 * 1024 * 1024;
        return new GpuInfo(adapter.Name, vendor, adapter.DedicatedMemory, integrated);
    }

    public void Dispose() => _timer.Stop();
}
