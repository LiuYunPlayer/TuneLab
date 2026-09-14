using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using TuneLab.Foundation;

namespace TuneLab.Extensions.Scripted;

// 防遗留第 4 防线（设计文档 §3.3）：spawn 记台账、退出销账；宿主猝死留下的记录由下一个实例
// （拿到 TuneLab.lock 之后）清扫——按 pid + 进程创建时间 + 可执行路径三重匹配防 PID 复用，
// 且只杀"路径落在 Extensions 运行时/包目录之下"的进程。
internal static class ScriptedRuntimeLedger
{
    sealed class Entry
    {
        public string package_id { get; set; } = string.Empty;
        public int pid { get; set; }
        public string exe { get; set; } = string.Empty;
        public long start_ticks { get; set; }     // Process.StartTime 的 UTC ticks；匹配不上 = 同 pid 新进程（复用）
    }

    static readonly object sLock = new();
    static List<Entry> sEntries = [];
    static string FilePath => Path.Combine(PathManager.ConfigsFolder, "scripted-pids.json");

    public static void Record(string packageId, int pid, string exe)
    {
        long startTicks;
        try { using var p = Process.GetProcessById(pid); startTicks = p.StartTime.ToUniversalTime().Ticks; }
        catch { return; }
        lock (sLock)
        {
            sEntries.RemoveAll(e => e.package_id == packageId && e.pid == pid);
            sEntries.Add(new Entry { package_id = packageId, pid = pid, exe = exe, start_ticks = startTicks });
            Save();
        }
    }

    public static void Complete(string packageId, int pid)
    {
        lock (sLock)
        {
            if (sEntries.RemoveAll(e => e.package_id == packageId && e.pid == pid) > 0)
                Save();
        }
    }

    // 清扫残留：返回杀掉的条数。任何一步失败只跳过该条，绝不因残留清单坏掉宿主启动。
    public static int SweepOrphans()
    {
        List<Entry> stale;
        lock (sLock)
        {
            if (!File.Exists(FilePath))
                return 0;
            try
            {
                sEntries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? [];
            }
            catch
            {
                sEntries = [];
                return 0;
            }
            stale = sEntries.ToList();
            sEntries.Clear();
            Save();
        }
        int killed = 0;
        foreach (var e in stale)
        {
            try
            {
                using var p = Process.GetProcessById(e.pid);
                if (p.StartTime.ToUniversalTime().Ticks != e.start_ticks)
                    continue;                                   // pid 已被复用：这不是我们留的人
                var imageName = TryGetExePath(p);
                if (imageName == null || !PathUnderExtensions(imageName))
                    continue;                                   // 三重匹配任何一环对不上都不动它
                p.Kill(true);
                killed++;
            }
            catch { /* 已自行退出：本就该从台账消失 */ }
        }
        return killed;
    }

    static string? TryGetExePath(Process p)
    {
        // MainModule 是 net8 最可靠的路径来源；拿不到（32/64 位差异、权限）就宁可放过——三重匹配宁可漏杀不误杀。
        try { return p.MainModule?.FileName; } catch { return null; }
    }

    static bool PathUnderExtensions(string candidate)
    {
        var root = Path.GetFullPath(PathManager.ExtensionsFolder) + Path.DirectorySeparatorChar;
        try { return Path.GetFullPath(candidate).StartsWith(root, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    static void Save()
    {
        try
        {
            PathManager.MakeSureExist(PathManager.ConfigsFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(sEntries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 台账尽力而为：丢了下一轮清扫兜底 */ }
    }
}

// packageId → SidecarHost 的进程级表（设计文档 §3.1）。M1 只被 format 工厂触碰。
internal static class SidecarRegistry
{
    static readonly Dictionary<string, SidecarHost> sHosts = new();
    static readonly object sLock = new();

    public static SidecarHost For(string packageId, string packageDir, RuntimeInfo runtime, string entry)
    {
        lock (sLock)
        {
            if (!sHosts.TryGetValue(packageId, out var host))
                sHosts[packageId] = host = new SidecarHost(packageId, packageDir, runtime, entry);
            return host;
        }
    }

    public static void ShutdownAll()
    {
        SidecarHost[] hosts;
        lock (sLock)
        {
            hosts = sHosts.Values.ToArray();
            sHosts.Clear();
        }
        foreach (var h in hosts)
            h.Shutdown();
    }

    public static void SweepStaleProcesses()
    {
        var n = ScriptedRuntimeLedger.SweepOrphans();
        if (n > 0)
            Log.Warning(string.Format(CultureInfo.InvariantCulture, "cleaned {0} orphaned sidecar process(es) from a previous run", n));
    }
}
