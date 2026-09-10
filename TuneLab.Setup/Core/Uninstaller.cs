using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;

namespace TuneLab.Setup.Core;

/// <summary>
/// 卸载：反注册关联与卸载表、删快捷方式、删掉当初装进去的那些文件。
/// 因卸载器自身位于目标目录内，无法边运行边删自己——先把自己复制到临时目录并从那里重启，
/// 由临时副本执行删除。
///
/// 【删的是文件，不是目录】安装目录是调用方给的（<c>-silent -dir D:\Apps</c> 这种），对它做递归删除
/// 会连用户自己放在那儿的东西一起带走。这里照 <see cref="InstallManifest"/> 只删安装器铺过的文件，
/// 目录仅在空了之后才删；剩下的一律留着，并把"留了什么"报出去。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Uninstaller
{
    /// <param name="report">每一行进展/结论。控制台与日志都要看，故由调用方决定送去哪儿。</param>
    public static void Run(string installDir, Action<string>? report = null)
    {
        // 若不是从临时目录运行，则自我搬迁后重启，避免"目录被占用删不掉"。
        string self = Environment.ProcessPath ?? string.Empty;
        string tempDir = Path.GetTempPath();
        bool runningFromInstallDir = self.StartsWith(Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase);

        if (runningFromInstallDir)
        {
            string relocated = Path.Combine(tempDir, $"TuneLab.Uninstall.{Guid.NewGuid():N}.exe");
            File.Copy(self, relocated, overwrite: true);
            Process.Start(new ProcessStartInfo(relocated)
            {
                Arguments = $"-uninstall \"{installDir}\" -silent",
                UseShellExecute = true,
            });
            return; // 交棒给临时副本
        }

        // 到这里说明是从临时目录运行的副本，安装目录里没有本进程占着的文件了。
        // 先把删除范围定下来再动手拆登记：范围拿不准时只拆登记、不删文件，用户至少还知道文件在哪。
        var plan = PlanDeletion(installDir, out var refusal);

        UninstallRegistry.Unregister();
        FileAssociation.Unregister();
        RemoveShortcuts();

        if (refusal != null)
            report?.Invoke(refusal);
        DeleteInstalledFiles(installDir, plan, report);
    }

    /// <summary>
    /// 定下这次要删哪些文件。<paramref name="refusal"/> 非空 = 有东西没敢删，原因要报给用户。
    /// 与 <see cref="DeleteInstalledFiles"/> 一样开成 internal：删错文件是这个类最坏的一种错，
    /// 这两步要能在临时目录上真跑一遍，而不是只靠纯函数推断（<see cref="Run"/> 本身要动注册表，跑不得）。
    /// </summary>
    internal static UninstallPlan PlanDeletion(string installDir, out string? refusal)
    {
        refusal = null;

        // 目录已经不在了（用户自己删过）：登记照拆，没有文件要删。
        if (!Directory.Exists(installDir))
            return new UninstallPlan([], []);

        if (InstallManifest.Read(installDir) is { } manifest)
            return InstallManifest.Plan(manifest, EnumerateRelativeFiles(installDir));

        // 没有清单 = 这份是旧安装器装的（那时还不记账）。它当初的行为就是整目录删，
        // 只在"目录名就是产品名"时照做——那种目录是安装器自己造的。
        if (InstallManifest.MayDeleteWholeDirectoryWithoutManifest(installDir))
            return new UninstallPlan(EnumerateRelativeFiles(installDir), []);

        // 既没有账，目录也不是我们造的名字：不猜。删错了拿不回来，留着只是占地方。
        // 连 Keep 都不填：这些文件是不是我们装的，这里本来就不知道，别让下面那句"这些不是 TuneLab 装的"
        // 替它下断言。原因由 refusal 一句说清。
        refusal = $"There is no install manifest in \"{installDir}\", so there is no record of which files were "
            + $"installed there, and the folder is not named {ProductInfo.ProductName}. Nothing in it was deleted, "
            + "rather than risk removing something that was never TuneLab's — look through the folder and delete "
            + "it by hand if it holds nothing but TuneLab.";
        return new UninstallPlan([], []);
    }

    static IReadOnlyList<string> EnumerateRelativeFiles(string dir)
    {
        var root = Path.GetFullPath(dir);
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();
    }

    static void RemoveShortcuts()
    {
        string linkName = ProductInfo.ProductName + ".lnk";

        string desktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), linkName);
        SafeDelete(desktop);

        string startMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductInfo.ProductName);
        if (Directory.Exists(startMenuDir))
            try { Directory.Delete(startMenuDir, true); } catch { /* best-effort */ }
    }

    internal static void DeleteInstalledFiles(string installDir, UninstallPlan plan, Action<string>? report)
    {
        var pending = new List<string>(plan.Delete);
        for (int attempt = 0; attempt < 10 && pending.Count > 0; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(500); // 等待仍占用的句柄释放（应用刚退出时 OS 可能还没放手）

            var stillPending = new List<string>();
            foreach (var relativePath in pending)
            {
                try { File.Delete(Path.Combine(installDir, relativePath)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { stillPending.Add(relativePath); }
            }
            pending = stillPending;
        }

        // 只删空目录，且自底向上——安装目录本身也只在空了之后才消失。
        PruneEmptyDirectories(installDir);

        int deleted = plan.Delete.Count - pending.Count;
        if (!Directory.Exists(installDir))
            report?.Invoke($"Removed \"{installDir}\".");
        else if (deleted > 0)
            report?.Invoke($"Removed {deleted} file(s) from \"{installDir}\".");

        if (pending.Count > 0)
            report?.Invoke($"{pending.Count} file(s) are still in use and could not be deleted: "
                + string.Join(", ", pending.Take(5)) + (pending.Count > 5 ? ", …" : ""));

        if (plan.Keep.Count > 0 && Directory.Exists(installDir))
            report?.Invoke($"{plan.Keep.Count} file(s) in that folder were not installed by TuneLab and were left alone: "
                + string.Join(", ", plan.Keep.Take(5)) + (plan.Keep.Count > 5 ? ", …" : ""));
    }

    static void PruneEmptyDirectories(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var sub in Directory.GetDirectories(dir))
            PruneEmptyDirectories(sub);

        try
        {
            if (Directory.GetFileSystemEntries(dir).Length == 0)
                Directory.Delete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort：删不掉就留着，上面会如实报出来 */ }
    }

    static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
