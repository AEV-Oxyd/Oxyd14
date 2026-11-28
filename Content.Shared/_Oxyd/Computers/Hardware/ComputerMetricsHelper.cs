using Content.Shared._Oxyd.Computers.Programs;

namespace Content.Shared._Oxyd.Computers.Hardware;

/// <summary>
/// Pure helper methods for reading program resource consumption against physical hardware stats.
/// </summary>
public static class ComputerMetricsHelper
{
    public static int GetProgramRamUsageMb(ProgramComponent prog) => Math.Max(1, prog.RamUsageMb);

    public static float GetProgramCpuUnits(ProgramComponent prog) =>
        Math.Max(0.5f, prog.CpuUsagePercent + Math.Max(0f, prog.DynamicCpuUsageBonus));

    public static float GetProgramCpuUsagePercent(ProgramComponent prog) => GetProgramCpuUnits(prog);

    public static float GetCpuCapacity(ComputerCpuComponent cpu) =>
        cpu.CpuCapacity > 0f ? cpu.CpuCapacity : Math.Max(50f, cpu.FrequencyMhz / 4f);
}
