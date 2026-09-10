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
        bool runningFromInstallDir = self.StartsWith(Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase);

        if (runningFromInstallDir)
        {
            string relocated = RelocateSelf(self);
            var copy = Process.Start(new ProcessStartInfo(relocated)
            {
                Arguments = $"-uninstall \"{installDir}\" -silent",
                UseShellExecute = false,
            });
            report?.Invoke($"Handed the uninstall to a copy of this program in \"{Path.GetDirectoryName(relocated)}\".");

            // 副本起不来是这条路上最坏的一种失败：GUI 子系统的进程，起不来一声不吭，用户点了卸载什么
            // 都不会发生，连日志都停在上一行。等它一小会儿——真干活至少要删掉一整个目录，不可能这么快
            // 就退——只要这么快就退了且退出码非零，就是没起来，如实说出来。
            if (copy != null && copy.WaitForExit(HandoffFailureWindow) && copy.ExitCode != 0)
                report?.Invoke($"That copy exited immediately with code {copy.ExitCode}, so nothing was removed "
                    + $"from \"{installDir}\". The uninstall entry and shortcuts are gone; delete the folder by hand.");
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

        ScheduleTempCopyCleanup();
    }

    /// <summary>
    /// 把卸载器搬到临时目录，返回搬过去那份的路径。
    ///
    /// 【搬的是一组文件，不是一个 exe】这是框架依赖构建：apphost 旁边必须有同名的 .dll 与
    /// .runtimeconfig.json/.deps.json，少一个就起不来（"The application to execute does not exist"）。
    /// 而它是 GUI 子系统的进程，起不来是彻底静默的——用户点了卸载，什么都不会发生，注册表登记和
    /// 文件全留着，连一行错都看不到。所以要连着同名的那几个文件一起搬，并且搬进一个独立的目录，
    /// 免得与临时目录里别人的同名文件撞上。
    /// </summary>
    static string RelocateSelf(string self)
    {
        string sourceDir = Path.GetDirectoryName(self) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(self);
        string tempDir = Path.Combine(Path.GetTempPath(), TempCopyPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        foreach (var file in Directory.GetFiles(sourceDir, baseName + ".*"))
            File.Copy(file, Path.Combine(tempDir, Path.GetFileName(file)), overwrite: true);

        return Path.Combine(tempDir, Path.GetFileName(self));
    }

    const string TempCopyPrefix = "TuneLab.Uninstall.";

    /// <summary>
    /// 等临时副本这么久（毫秒）：这段时间里退出的只可能是根本没起来。
    /// 别调大——这段时间里本进程还活着，它开着自己的 exe/dll，副本删这两个文件会一直失败
    /// （靠 DeleteInstalledFiles 的重试兜住，但没必要让它多等）。
    /// </summary>
    const int HandoffFailureWindow = 2000;

    /// <summary>空目录清理最多走几趟。见 DeleteInstalledFiles 里为什么不能只走一趟。</summary>
    const int MaxPrunePasses = 8;

    /// <summary>
    /// 临时副本删不掉自己所在的那个目录——它正跑在里面。交给一个短命的 cmd，等本进程退出再清。
    /// 不清的话每卸载一次就在 %temp% 里留下一份约 1 MB 的残骸。
    /// </summary>
    static void ScheduleTempCopyCleanup()
    {
        string dir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        // 只清我们自己造的那种目录。这条判断是这段代码唯一的安全边界，别放宽。
        if (!Path.GetFileName(dir).StartsWith(TempCopyPrefix, StringComparison.Ordinal))
            return;

        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & rd /s /q \"{dir}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch { /* best-effort：清不掉只是留下一份临时文件 */ }
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
        //
        // 【为什么要走几趟】File.Delete 成功不等于目录里立刻少一条：文件若还被别的进程以共享删除方式
        // 开着（交棒过来时那个进程还会活几秒，它开着自己的 exe 和 dll），删除是"待定"的，等最后一个
        // 句柄关闭才真的落地。只走一趟就会看见一个"还不空"的目录而放过它，留下一副空壳。
        for (int pass = 0; ; pass++)
        {
            PruneEmptyDirectories(installDir);

            if (!Directory.Exists(installDir) || pass >= MaxPrunePasses - 1)
                break;

            // 我们的文件都真的不在了 → 目录不会再变空，不必再等（这是"目录里还有用户自己的东西"
            // 那条常见路径，一趟就够）。
            if (!plan.Delete.Any(rel => File.Exists(Path.Combine(installDir, rel))))
                break;

            Thread.Sleep(500);
        }

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
