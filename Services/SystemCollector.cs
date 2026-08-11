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

public class TelemetryData
{
    public string Hostname { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string CpuModel { get; set; } = string.Empty;
    public int CpuCores { get; set; }
    public double CpuUsagePercent { get; set; }
    public long RamTotalBytes { get; set; }
    public long RamUsedBytes { get; set; }
    public double RamUsagePercent { get; set; }
    public long DiskTotalBytes { get; set; }
    public long DiskUsedBytes { get; set; }
    public double DiskUsagePercent { get; set; }
    public double UptimeSeconds { get; set; }
    public List<string> IpAddresses { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class SystemCollector
{
    private static (long idle, long total) _prevCpuStats;

    public static async Task<TelemetryData> CollectAsync()
    {
        var data = new TelemetryData
        {
            Hostname = Environment.MachineName,
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
        data.CpuUsagePercent = await GetCpuUsagePercentAsync();

        return data;
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

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable))
                {
                    totalBytes += drive.TotalSize;
                    freeBytes += drive.AvailableFreeSpace;
                }
            }
        }
        catch { }

        data.DiskTotalBytes = totalBytes;
        data.DiskUsedBytes = Math.Max(0, totalBytes - freeBytes);
        if (totalBytes > 0)
        {
            data.DiskUsagePercent = Math.Round((double)data.DiskUsedBytes / totalBytes * 100.0, 1);
        }
    }

    private static async Task<double> GetCpuUsagePercentAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/stat"))
        {
            try
            {
                var cur = ReadLinuxCpuStat();
                if (_prevCpuStats.total > 0 && cur.total > _prevCpuStats.total)
                {
                    long totalDiff = cur.total - _prevCpuStats.total;
                    long idleDiff = cur.idle - _prevCpuStats.idle;
                    double usage = (1.0 - (double)idleDiff / totalDiff) * 100.0;
                    _prevCpuStats = cur;
                    return Math.Round(Math.Clamp(usage, 0.0, 100.0), 1);
                }

                _prevCpuStats = cur;
                await Task.Delay(100);
                var cur2 = ReadLinuxCpuStat();
                long totalDiff2 = cur2.total - cur.total;
                long idleDiff2 = cur2.idle - cur.idle;
                _prevCpuStats = cur2;
                if (totalDiff2 > 0)
                {
                    double usage = (1.0 - (double)idleDiff2 / totalDiff2) * 100.0;
                    return Math.Round(Math.Clamp(usage, 0.0, 100.0), 1);
                }
            }
            catch { }
        }

        return 5.0; // Minimal default fallback
    }

    private static (long idle, long total) ReadLinuxCpuStat()
    {
        var firstLine = File.ReadLines("/proc/stat").FirstOrDefault();
        if (string.IsNullOrEmpty(firstLine)) return (0, 0);

        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
