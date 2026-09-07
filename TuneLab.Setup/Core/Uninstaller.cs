using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;

namespace TuneLab.Setup.Core;

/// <summary>
/// 卸载：反注册关联与卸载表、删快捷方式、清目标目录。
/// 因卸载器自身位于目标目录内，无法边运行边删自己——先把自己复制到临时目录并从那里重启，
/// 由临时副本删除整个安装目录。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Uninstaller
{
    public static void Run(string installDir)
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

        // 到这里说明是从临时目录运行的副本，可安全删除安装目录。
        UninstallRegistry.Unregister();
        FileAssociation.Unregister();
        RemoveShortcuts();
        DeleteInstallDir(installDir);
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

    static void DeleteInstallDir(string installDir)
    {
        for (int attempt = 0; attempt < 10 && Directory.Exists(installDir); attempt++)
        {
            try
            {
                Directory.Delete(installDir, true);
                return;
            }
            catch
            {
                Thread.Sleep(500); // 等待仍占用的句柄释放
            }
        }
    }

    static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
