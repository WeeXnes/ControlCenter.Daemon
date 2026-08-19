using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ControlCenter.Daemon.Services;

public class DiskStatsInfo
{
    public string Name { get; set; } = string.Empty;
    public string MountPoint { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public double UsagePercent { get; set; }
    public string DriveType { get; set; } = string.Empty;
}

public class TelemetryData
{
    public string Hostname { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string CpuModel { get; set; } = string.Empty;
    public int CpuCores { get; set; }
    public double CpuUsagePercent { get; set; }
    public List<double> CpuCoreUsages { get; set; } = new();
    public long RamTotalBytes { get; set; }
    public long RamUsedBytes { get; set; }
    public long RamAvailableBytes { get; set; }
    public double RamUsagePercent { get; set; }
    public long DiskTotalBytes { get; set; }
    public long DiskUsedBytes { get; set; }
    public double DiskUsagePercent { get; set; }
    public List<DiskStatsInfo> Disks { get; set; } = new();
    public double UptimeSeconds { get; set; }
    public List<string> IpAddresses { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class SystemCollector
{
    private static (long idle, long total) _prevCpuStats;
    private static readonly Dictionary<int, (long idle, long total)> _prevPerCoreCpuStats = new();

    public static async Task<TelemetryData> CollectAsync()
    {
        var data = new TelemetryData
        {
            Hostname = Environment.MachineName,
            UserName = GetActiveUsername(),
            Os = RuntimeInformation.OSDescription,
            CpuCores = Environment.ProcessorCount,
            CpuModel = GetCpuModelName(),
            IpAddresses = GetLocalIpAddresses(),
            UptimeSeconds = GetUptimeSeconds()
        };

        // Collect Memory Stats
        CollectRamStats(data);

        // Collect Disk Stats
        CollectDiskStats(data);

        // Collect CPU Stats
        await CollectCpuStatsAsync(data);

        return data;
    }

    public static string GetActiveUsername()
    {
        // 1. If executed under sudo, SUDO_USER contains the actual human user
        string? sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
        if (!string.IsNullOrWhiteSpace(sudoUser) && sudoUser != "root")
        {
            return sudoUser.Trim();
        }

        // 2. Check LOGNAME or USER environment variables
        string? logname = Environment.GetEnvironmentVariable("LOGNAME") ?? Environment.GetEnvironmentVariable("USER");
        if (!string.IsNullOrWhiteSpace(logname) && logname != "root")
        {
            return logname.Trim();
        }

        // 3. Environment.UserName
        string user = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(user) && user != "root")
        {
            return user.Trim();
        }

        // 4. If running as root on Linux, check /home for active non-root desktop user
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && Directory.Exists("/home"))
        {
            try
            {
                var homeDirs = Directory.GetDirectories("/home")
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n) && n != "lost+found" && !n.StartsWith("."))
                    .ToArray();

                if (homeDirs.Length == 1 && !string.IsNullOrEmpty(homeDirs[0]))
                {
                    return homeDirs[0]!;
                }
            }
            catch { }
        }

        return !string.IsNullOrWhiteSpace(user) ? user : "root";
    }

    private static string GetCpuModelName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/cpuinfo"))
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                {
                    if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':');
                        if (parts.Length > 1) return parts[1].Trim();
                    }
                }
            }
            catch { }
        }
        return $"{RuntimeInformation.ProcessArchitecture} ({Environment.ProcessorCount} Cores)";
    }

    private static List<string> GetLocalIpAddresses()
    {
        var ips = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        ips.Add(ip.Address.ToString());
                    }
                }
            }
        }
        catch { }

        if (ips.Count == 0) ips.Add("127.0.0.1");
        return ips;
    }

    private static double GetUptimeSeconds()
    {
        return Environment.TickCount64 / 1000.0;
    }

    private static void CollectRamStats(TelemetryData data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/meminfo"))
        {
            try
            {
                long memTotal = 0;
                long memAvailable = 0;

                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:"))
                    {
                        memTotal = ParseKbLine(line) * 1024;
                    }
                    else if (line.StartsWith("MemAvailable:"))
                    {
                        memAvailable = ParseKbLine(line) * 1024;
                    }
                }

                if (memTotal > 0)
                {
                    data.RamTotalBytes = memTotal;
                    data.RamAvailableBytes = memAvailable;
                    data.RamUsedBytes = Math.Max(0, memTotal - memAvailable);
                    data.RamUsagePercent = Math.Round((double)data.RamUsedBytes / memTotal * 100.0, 1);
                    return;
                }
            }
            catch { }
        }

        // Fallback for Windows/macOS or if /proc/meminfo fails
        var gcInfo = GC.GetGCMemoryInfo();
        data.RamTotalBytes = gcInfo.TotalAvailableMemoryBytes;
        data.RamUsedBytes = Environment.WorkingSet;
        data.RamAvailableBytes = Math.Max(0, data.RamTotalBytes - data.RamUsedBytes);
        if (data.RamTotalBytes > 0)
        {
            data.RamUsagePercent = Math.Round((double)data.RamUsedBytes / data.RamTotalBytes * 100.0, 1);
        }
    }

    private static long ParseKbLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
        {
            return kb;
        }
        return 0;
    }

    private static void CollectDiskStats(TelemetryData data)
    {
        long totalBytes = 0;
        long freeBytes = 0;
        var diskList = new List<DiskStatsInfo>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable))
                {
                    long dTotal = drive.TotalSize;
                    long dFree = drive.AvailableFreeSpace;
                    long dUsed = Math.Max(0, dTotal - dFree);
                    double dPercent = dTotal > 0 ? Math.Round((double)dUsed / dTotal * 100.0, 1) : 0;

                    totalBytes += dTotal;
                    freeBytes += dFree;

                    diskList.Add(new DiskStatsInfo
                    {
                        Name = drive.Name,
                        MountPoint = drive.RootDirectory.FullName,
                        TotalBytes = dTotal,
                        UsedBytes = dUsed,
                        UsagePercent = dPercent,
                        DriveType = drive.DriveType.ToString()
                    });
                }
            }
        }
        catch { }

        data.Disks = diskList;
        data.DiskTotalBytes = totalBytes;
        data.DiskUsedBytes = Math.Max(0, totalBytes - freeBytes);
        if (totalBytes > 0)
        {
            data.DiskUsagePercent = Math.Round((double)data.DiskUsedBytes / totalBytes * 100.0, 1);
        }
    }

    private static async Task CollectCpuStatsAsync(TelemetryData data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/stat"))
        {
            try
            {
                var (overallStat, coreStats) = ReadLinuxCpuStats();
                if (_prevCpuStats.total > 0 && overallStat.total > _prevCpuStats.total)
                {
                    long totalDiff = overallStat.total - _prevCpuStats.total;
                    long idleDiff = overallStat.idle - _prevCpuStats.idle;
                    double usage = (1.0 - (double)idleDiff / totalDiff) * 100.0;
                    data.CpuUsagePercent = Math.Round(Math.Clamp(usage, 0.0, 100.0), 1);
                    _prevCpuStats = overallStat;

                    var coreUsages = new List<double>();
                    for (int i = 0; i < coreStats.Count; i++)
                    {
                        var c = coreStats[i];
                        if (_prevPerCoreCpuStats.TryGetValue(i, out var prevCore) && c.total > prevCore.total)
                        {
                            long cTotDiff = c.total - prevCore.total;
                            long cIdleDiff = c.idle - prevCore.idle;
                            double cUsage = (1.0 - (double)cIdleDiff / cTotDiff) * 100.0;
                            coreUsages.Add(Math.Round(Math.Clamp(cUsage, 0.0, 100.0), 1));
                        }
                        else
                        {
                            coreUsages.Add(data.CpuUsagePercent);
                        }
                        _prevPerCoreCpuStats[i] = c;
                    }
                    data.CpuCoreUsages = coreUsages;
                    return;
                }

                _prevCpuStats = overallStat;
                for (int i = 0; i < coreStats.Count; i++)
                {
                    _prevPerCoreCpuStats[i] = coreStats[i];
                }

                await Task.Delay(100);

                var (overallStat2, coreStats2) = ReadLinuxCpuStats();
                long totalDiff2 = overallStat2.total - overallStat.total;
                long idleDiff2 = overallStat2.idle - overallStat.idle;
                _prevCpuStats = overallStat2;

                if (totalDiff2 > 0)
                {
                    double usage = (1.0 - (double)idleDiff2 / totalDiff2) * 100.0;
                    data.CpuUsagePercent = Math.Round(Math.Clamp(usage, 0.0, 100.0), 1);
                }
                else
                {
                    data.CpuUsagePercent = 5.0;
                }

                var coreUsages2 = new List<double>();
                for (int i = 0; i < coreStats2.Count; i++)
                {
                    var c2 = coreStats2[i];
                    var prevC = i < coreStats.Count ? coreStats[i] : (0, 0);
                    long cTotDiff = c2.total - prevC.total;
                    long cIdleDiff = c2.idle - prevC.idle;
                    _prevPerCoreCpuStats[i] = c2;

                    if (cTotDiff > 0)
                    {
                        double cUsage = (1.0 - (double)cIdleDiff / cTotDiff) * 100.0;
                        coreUsages2.Add(Math.Round(Math.Clamp(cUsage, 0.0, 100.0), 1));
                    }
                    else
                    {
                        coreUsages2.Add(data.CpuUsagePercent);
                    }
                }
                data.CpuCoreUsages = coreUsages2;
                return;
            }
            catch { }
        }

        data.CpuUsagePercent = 5.0;
    }

    private static ((long idle, long total) overall, List<(long idle, long total)> cores) ReadLinuxCpuStats()
    {
        (long idle, long total) overall = (0, 0);
        var cores = new List<(long idle, long total)>();

        foreach (var line in File.ReadLines("/proc/stat"))
        {
            if (line.StartsWith("cpu "))
            {
                overall = ParseCpuStatLine(line);
            }
            else if (line.StartsWith("cpu") && line.Length > 3 && char.IsDigit(line[3]))
            {
                cores.Add(ParseCpuStatLine(line));
            }
            else if (!line.StartsWith("cpu"))
            {
                break;
            }
        }

        return (overall, cores);
    }

    private static (long idle, long total) ParseCpuStatLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) return (0, 0);

        long user = long.Parse(parts[1]);
        long nice = long.Parse(parts[2]);
        long system = long.Parse(parts[3]);
        long idle = long.Parse(parts[4]);
        long iowait = parts.Length > 5 ? long.Parse(parts[5]) : 0;
        long irq = parts.Length > 6 ? long.Parse(parts[6]) : 0;
        long softirq = parts.Length > 7 ? long.Parse(parts[7]) : 0;
        long steal = parts.Length > 8 ? long.Parse(parts[8]) : 0;

        long totalIdle = idle + iowait;
        long totalNonIdle = user + nice + system + irq + softirq + steal;
        long total = totalIdle + totalNonIdle;

        return (totalIdle, total);
    }
}
