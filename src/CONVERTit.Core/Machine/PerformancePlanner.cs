namespace CONVERTit.Core.Machine;

/// <summary>How much of the PC CONVERTit may use. Auto follows power source and hardware.</summary>
public enum PerformanceProfile { Auto, Eco, Balanced, Maximum, Custom }

public enum GpuVendor { Nvidia, Amd, Intel, Other }

/// <summary>Priority of conversion processes.</summary>
public enum JobPriority { Normal, BelowNormal, Idle }

public sealed record GpuInfo(string Name, GpuVendor Vendor, long DedicatedMemory, bool Integrated);

/// <summary>What the system detection found (Windows: power status, CPU topology, memory, DXGI adapters).</summary>
public sealed record MachineInfo
{
    public static MachineInfo Unknown { get; } = new() { LogicalCores = Environment.ProcessorCount };

    public string CpuName { get; init; } = "";

    public int LogicalCores { get; init; }

    public int PhysicalCores { get; init; }

    /// <summary>Intel hybrid CPUs: efficiency cores (0 = none or unknown).</summary>
    public int EfficiencyCores { get; init; }

    public long MemoryBytes { get; init; }

    /// <summary>A battery exists (notebook, tablet).</summary>
    public bool HasBattery { get; init; }

    public bool OnBattery { get; init; }

    /// <summary>0–100, -1 = unknown.</summary>
    public int BatteryPercent { get; init; } = -1;

    /// <summary>Windows energy saver / battery saver is on.</summary>
    public bool EnergySaver { get; init; }

    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];

    public bool HasDedicatedGpu => Gpus.Any(g => !g.Integrated);
}

/// <summary>Resource settings derived from a profile.</summary>
public sealed record ResourcePlan(
    PerformanceProfile Profile,
    string ReasonKey,
    int Threads,
    int MediaParallel,
    int ImageParallel,
    JobPriority Priority,
    bool EcoQos,
    bool GpuTurbo,
    bool PauseOnLowBattery);

/// <summary>Picks the profile for Auto and turns a profile into thread, parallelism and priority values (pure, unit-tested).</summary>
public static class PerformancePlanner
{
    /// <summary>Battery level below which the Eco profile pauses the queue.</summary>
    public const int LowBatteryPercent = 20;

    /// <summary>Auto: battery or energy saver → Eco; notebook on mains → Balanced; strong desktop → Maximum.</summary>
    public static (PerformanceProfile Profile, string ReasonKey) Resolve(PerformanceProfile chosen, MachineInfo m)
    {
        if (chosen != PerformanceProfile.Auto)
        {
            return (chosen, "ReasonChosen");
        }

        if (m.OnBattery)
        {
            return (PerformanceProfile.Eco, "ReasonOnBattery");
        }

        if (m.EnergySaver)
        {
            return (PerformanceProfile.Eco, "ReasonEnergySaver");
        }

        if (m.HasBattery)
        {
            return (PerformanceProfile.Balanced, "ReasonNotebookMains");
        }

        return m.LogicalCores >= 12 || m.HasDedicatedGpu
            ? (PerformanceProfile.Maximum, "ReasonStrongDesktop")
            : (PerformanceProfile.Balanced, "ReasonDesktop");
    }

    /// <summary>
    /// Resource values of a profile. Custom keeps the manual values. Quality is never touched: Eco only takes
    /// longer (fewer threads, low priority, EcoQoS), the files stay the same.
    /// </summary>
    public static ResourcePlan Plan(PerformanceProfile chosen, MachineInfo m,
        int customMediaParallel, int customImageParallel, JobPriority customPriority, bool customTurbo)
    {
        var (profile, reason) = Resolve(chosen, m);
        var cores = Math.Max(1, m.LogicalCores);
        return profile switch
        {
            PerformanceProfile.Eco => new ResourcePlan(profile, reason,
                Threads: Math.Max(2, cores / 2), MediaParallel: 1, ImageParallel: Math.Min(2, cores),
                JobPriority.Idle, EcoQos: true, GpuTurbo: false, PauseOnLowBattery: true),
            PerformanceProfile.Maximum => new ResourcePlan(profile, reason,
                Threads: 0, MediaParallel: cores >= 16 ? 2 : 1, ImageParallel: Math.Clamp(cores, 1, 16),
                JobPriority.Normal, EcoQos: false, GpuTurbo: true, PauseOnLowBattery: false),
            PerformanceProfile.Custom => new ResourcePlan(profile, reason,
                Threads: 0, MediaParallel: Math.Clamp(customMediaParallel, 1, 4), ImageParallel: Math.Clamp(customImageParallel, 1, 16),
                customPriority, EcoQos: false, GpuTurbo: customTurbo, PauseOnLowBattery: false),
            _ => new ResourcePlan(PerformanceProfile.Balanced, reason,
                Threads: cores > 4 ? cores - 2 : 0, MediaParallel: 1, ImageParallel: Math.Clamp(cores / 2, 1, 8),
                JobPriority.BelowNormal, EcoQos: false, GpuTurbo: true, PauseOnLowBattery: false)
        };
    }
}
