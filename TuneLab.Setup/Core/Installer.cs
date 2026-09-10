using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Setup.Core;

/// <summary>安装/卸载进度回报：整体完成度 [0,1] + 一行状态文案。</summary>
internal readonly record struct InstallStatus(double Fraction, string Message);

/// <summary>
/// 安装编排：铺文件 → 落 tunelab 转发入口 → 建快捷方式 → 关联扩展名 → 注册卸载表。
/// 所有落地都在每用户目录 / HKCU，故全程无需管理员。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Installer
{
    readonly InstallOptions mOptions;

    public Installer(InstallOptions options) => mOptions = options;

    public async Task InstallAsync(IProgress<InstallStatus>? progress, CancellationToken ct)
    {
        // 载荷 = 向导自身所在目录（外层 stub 已把 app + 向导解压到此临时暂存目录）。
        // 整目录铺到安装目录，向导 exe 一并落地成为卸载器/更新器，与 app 共享同一份 Avalonia/Skia。
        string baseDir = AppContext.BaseDirectory;
        if (!File.Exists(Path.Combine(baseDir, ProductInfo.ExecutableName)))
            throw new InvalidOperationException(
                $"Install payload not found: {ProductInfo.ExecutableName} is missing next to the installer.");
        IPayloadProvider payload = new DirectoryPayloadProvider(baseDir);

        try
        {
            string installDir = mOptions.InstallDir;
            Directory.CreateDirectory(installDir);

            // 若 TuneLab 正在运行，等它退出（复用主程序的 lock 文件约定）——不能覆盖正被占用的文件。
            // 向导里这一步可能停留一会儿，故先把「在等什么」报出去，否则进度条看着像卡死了。
            if (File.Exists(LockFilePath))
                progress?.Report(new InstallStatus(0, "Waiting for TuneLab to close…"));
            await WaitForAppExitAsync(ct, mOptions.WaitForAppExitTimeout);

            // 1) 铺文件（0 → 0.85）
            long total = payload.UncompressedSize;
            var extractProgress = new Progress<ExtractProgress>(p =>
            {
                double frac = p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal : 0;
                progress?.Report(new InstallStatus(0.85 * frac, $"Installing files… {Path.GetFileName(p.CurrentEntry)}"));
            });
            await Task.Run(() => payload.ExtractTo(installDir, extractProgress, ct), ct);

            // 卸载器/更新器 TuneLab.Setup.exe 已随目录一并铺入安装目录，无需单独复制。

            // 2) `tunelab` 转发入口：命令面的文本里教的就是这个名字，它必须真的存在（见 CommandLineEntry）。
            //    每次安装与更新都重写——它是产品的一部分，不是按用户选择创建的快捷方式。
            progress?.Report(new InstallStatus(0.88, "Writing the tunelab command…"));
            bool commandLineWritten = CommandLineEntry.Write(installDir);

            // 3-4) 快捷方式 + 文件关联：仅首次安装。更新模式跳过，保留用户当初的选择。
            if (!mOptions.IsUpdate)
            {
                progress?.Report(new InstallStatus(0.90, "Creating shortcuts…"));
                CreateShortcuts(installDir);

                if (mOptions.RegisterFileAssociations)
                {
                    progress?.Report(new InstallStatus(0.94, "Registering file associations…"));
                    FileAssociation.Register(installDir);
                }
            }

            // 5) 安装清单：把"这次往这个目录里放了哪些文件"记下来，卸载照着它删。安装目录是调用方给的，
            //    没有这份账就只能对整个目录做递归删除——那会连用户自己放在那儿的东西一起带走。
            progress?.Report(new InstallStatus(0.96, "Recording what was installed…"));
            InstallManifest.Write(installDir, CollectInstalledFiles(payload, installDir, commandLineWritten));

            // 6) 卸载注册表（0.96 → 1.0）
            progress?.Report(new InstallStatus(0.98, "Registering uninstall entry…"));
            UninstallRegistry.Register(installDir, total, DateTime.Now.ToString("yyyyMMdd"));

            progress?.Report(new InstallStatus(1.0, "Done."));
        }
        finally
        {
            (payload as IDisposable)?.Dispose();
        }
    }

    // 这次安装之后，安装目录里哪些文件是我们的。
    // internal：认领错一个文件，卸载就少删或多删一个（多删的那头是用户的东西），值得单独钉住。
    internal static IEnumerable<string> CollectInstalledFiles(IPayloadProvider payload, string installDir, bool commandLineWritten)
    {
        var files = new List<string>(payload.EnumerateEntries());

        // tunelab.cmd 不在载荷里，是上一步现写的。
        if (commandLineWritten)
            files.Add(CommandLineEntry.FileName);

        // 清单自己也算：不认领它，卸载完它会孤零零留在那儿，把本该消失的目录撑着删不掉。
        files.Add(InstallManifest.FileName);

        // 更新：上一版装过、这一版不再包含的文件仍然是我们装的（安装不清理旧文件，它们还躺在目录里）。
        // 不并进来的话，一次更新就能把它们变成"来路不明、不敢删"的东西。
        if (InstallManifest.Read(installDir) is { } previous)
            files.AddRange(previous);

        return files;
    }

    public void Launch()
    {
        string exe = Path.Combine(mOptions.InstallDir, ProductInfo.ExecutableName);
        if (File.Exists(exe))
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = mOptions.InstallDir });
    }

    void CreateShortcuts(string installDir)
    {
        string exePath = Path.Combine(installDir, ProductInfo.ExecutableName);
        string linkName = ProductInfo.ProductName + ".lnk";

        if (mOptions.CreateStartMenuShortcut)
        {
            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                ProductInfo.ProductName, linkName);
            ShortcutHelper.Create(startMenu, exePath, installDir, ProductInfo.ProductName, exePath);
        }

        if (mOptions.CreateDesktopShortcut)
        {
            string desktop = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), linkName);
            ShortcutHelper.Create(desktop, exePath, installDir, ProductInfo.ProductName, exePath);
        }
    }

    // 主程序 lock 文件路径（每用户 AppData，与主程序约定一致）。
    static string LockFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ProductInfo.ProductName, ProductInfo.ProductName + ".lock");

    // 目标机上 TuneLab 是否正在运行：lock 文件被独占持有即运行中；能独占打开的是残留、视为未运行。
    public static bool IsAppRunning()
    {
        if (!File.Exists(LockFilePath))
            return false;
        try
        {
            using var _ = File.Open(LockFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    // timeout = null 时一直等（向导：用户看得见，也能取消）；给了值就到点报错——见 InstallOptions。
    static async Task WaitForAppExitAsync(CancellationToken ct, TimeSpan? timeout)
    {
        var deadline = timeout is { } span ? DateTime.UtcNow + span : (DateTime?)null;
        while (File.Exists(LockFilePath))
        {
            ct.ThrowIfCancellationRequested();
            if (deadline is { } due && DateTime.UtcNow > due)
                throw new TimeoutException(
                    "TuneLab is still running, so its files cannot be replaced. Close it and run this again.");
            // lock 文件可能只是残留（被独占则会抛），尝试独占打开判断是否真被占用。
            try
            {
                using var _ = File.Open(LockFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                // 能独占 → 是残留，删掉继续。
                _.Dispose();
                File.Delete(LockFilePath);
                break;
            }
            catch (IOException)
            {
                await Task.Delay(500, ct);
            }
        }
    }
}
