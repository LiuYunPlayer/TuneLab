using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Setup.Core;

/// <summary>安装/卸载进度回报：整体完成度 [0,1] + 一行状态文案。</summary>
internal readonly record struct InstallStatus(double Fraction, string Message);

/// <summary>
/// 安装编排：铺文件 → 写卸载器副本 → 建快捷方式 → 关联扩展名 → 注册卸载表。
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

            // 若目标目录里 TuneLab 正在运行，等它退出（复用主程序的 lock 文件约定）。
            await WaitForAppExitAsync(ct);

            // 1) 铺文件（0 → 0.85）
            long total = payload.UncompressedSize;
            var extractProgress = new Progress<ExtractProgress>(p =>
            {
                double frac = p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal : 0;
                progress?.Report(new InstallStatus(0.85 * frac, $"Installing files… {Path.GetFileName(p.CurrentEntry)}"));
            });
            await Task.Run(() => payload.ExtractTo(installDir, extractProgress, ct), ct);

            // 卸载器/更新器 TuneLab.Setup.exe 已随目录一并铺入安装目录，无需单独复制。

            // 3) 快捷方式（0.88 → 0.92）
            progress?.Report(new InstallStatus(0.90, "Creating shortcuts…"));
            CreateShortcuts(installDir);

            // 4) 文件关联（0.92 → 0.96）
            if (mOptions.RegisterFileAssociations)
            {
                progress?.Report(new InstallStatus(0.94, "Registering file associations…"));
                FileAssociation.Register(installDir);
            }

            // 5) 卸载注册表（0.96 → 1.0）
            progress?.Report(new InstallStatus(0.98, "Registering uninstall entry…"));
            UninstallRegistry.Register(installDir, total, DateTime.Now.ToString("yyyyMMdd"));

            progress?.Report(new InstallStatus(1.0, "Done."));
        }
        finally
        {
            (payload as IDisposable)?.Dispose();
        }
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

    static async Task WaitForAppExitAsync(CancellationToken ct)
    {
        string lockFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ProductInfo.ProductName, ProductInfo.ProductName + ".lock");

        while (File.Exists(lockFile))
        {
            ct.ThrowIfCancellationRequested();
            // lock 文件可能只是残留（被独占则会抛），尝试独占打开判断是否真被占用。
            try
            {
                using var _ = File.Open(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                // 能独占 → 是残留，删掉继续。
                _.Dispose();
                File.Delete(lockFile);
                break;
            }
            catch (IOException)
            {
                await Task.Delay(500, ct);
            }
        }
    }
}
