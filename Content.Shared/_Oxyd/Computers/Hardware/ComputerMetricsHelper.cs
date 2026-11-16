using Content.Shared._Oxyd.Computers.Programs;

namespace Content.Shared._Oxyd.Computers.Hardware;

/// <summary>
/// Pure helper methods for reading program resource consumption against physical hardware stats.
/// </summary>
public static class ComputerMetricsHelper
{
    public static int GetProgramRamUsageMb(ProgramComponent prog) => Math.Max(1, prog.RamUsageMb);

    public static float GetProgramCpuUsagePercent(ProgramComponent prog) => Math.Max(0.5f, prog.CpuUsagePercent);
}
