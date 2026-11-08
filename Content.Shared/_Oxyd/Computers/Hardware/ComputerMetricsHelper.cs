using System.Linq;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;

namespace Content.Shared._Oxyd.Computers.Hardware;

public static class ComputerMetricsHelper
{
    public static void CalculateMetrics(
        int openProgramCount,
        ComputerStatsComponent? stats,
        FileSystemComponent? fs,
        NetworkBandwidthComponent? bw,
        out float cpuPercent,
        out int ramUsedMb,
        out int ramTotalMb,
        out int storageUsedBytes,
        out int storageTotalBytes,
        out bool isSaturated,
        out int bandwidthBps,
        double curTime = 0,
        float openProgramsCustomCpu = 0f)
    {
        ramTotalMb = stats?.RamMb ?? 64;
        storageTotalBytes = fs?.MaxStorageBytes ?? (stats?.StorageBytes ?? 1048576);
        storageUsedBytes = fs != null ? fs.Files.Values.Sum(f => f.Size) : 0;
        isSaturated = bw?.IsSaturated ?? false;

        // RAM: base OS (16MB) + open programs (8MB each) + cache for storage
        var storageCacheMb = storageUsedBytes / (1024 * 128);
        var minRam = Math.Min(16, Math.Max(0, ramTotalMb));
        var maxRam = Math.Max(0, ramTotalMb);
        ramUsedMb = maxRam <= 0 ? 0 : Math.Clamp(16 + (openProgramCount * 8) + storageCacheMb, minRam, maxRam);

        // Fluctuate CPU slightly by a few percentages (±1.5% to 3.5%) based on time
        var jitter = (float)(Math.Sin(curTime * 1.4) * 2.1 + Math.Sin(curTime * 0.7 + 0.9) * 1.4);
        var baseCpu = Math.Clamp(6.0f + jitter, 2.0f, 15.0f);

        // Each open program has a set CPU load (default 4.5% or custom)
        var programLoad = openProgramsCustomCpu > 0 ? openProgramsCustomCpu : (openProgramCount * 4.5f);
        var netLoad = isSaturated ? 55f : (bw != null && bw.CurrentBytesReceived > 1000 ? 5f : 0f);

        cpuPercent = Math.Clamp(baseCpu + programLoad + netLoad, 2f, 100f);
        bandwidthBps = bw?.MaxBps ?? (stats?.NetworkBandwidthBps ?? 100000);
    }

    public static int GetProgramSize(string? protoId) => protoId switch
    {
        "CrewManifestCartridge" => 262144, // 256 KB
        "NanoTaskCartridge" => 196608,     // 192 KB
        "NewsReaderCartridge" => 131072,   // 128 KB
        "TerminalCartridge" => 98304,      // 96 KB
        "DownloaderCartridge" => 81920,    // 80 KB
        "FileBrowserCartridge" => 65536,   // 64 KB
        "NotekeeperCartridge" => 49152,    // 48 KB
        _ => 65536                         // 64 KB default
    };
}
