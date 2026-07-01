using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TuneLab.Setup.Core;

/// <summary>
/// 往 HKCU 的 Uninstall 表写/删产品条目，让 TuneLab 出现在"添加或删除程序"里。
/// 每用户级（HKCU），故无需管理员权限。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class UninstallRegistry
{
    const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static void Register(string installDir, long estimatedSizeBytes, string installDate)
    {
        using var root = Registry.CurrentUser.CreateSubKey(UninstallRoot);
        using var key = root.CreateSubKey(ProductInfo.UninstallKeyName);

        string exePath = Path.Combine(installDir, ProductInfo.ExecutableName);
        // 卸载器 = 随安装目录一并铺入的向导 TuneLab.Setup.exe（-uninstall 模式会自复制到临时目录再删本目录）。
        string uninstaller = Path.Combine(installDir, "TuneLab.Setup.exe");

        key.SetValue("DisplayName", ProductInfo.ProductName);
        key.SetValue("DisplayVersion", ProductInfo.VersionString);
        key.SetValue("Publisher", ProductInfo.Publisher);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("UninstallString", $"\"{uninstaller}\" -uninstall \"{installDir}\"");
        key.SetValue("QuietUninstallString", $"\"{uninstaller}\" -uninstall \"{installDir}\" -silent");
        key.SetValue("URLInfoAbout", ProductInfo.HelpUrl);
        key.SetValue("InstallDate", installDate);
        key.SetValue("EstimatedSize", (int)Math.Max(1, estimatedSizeBytes / 1024), RegistryValueKind.DWord); // KB
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void Unregister()
    {
        using var root = Registry.CurrentUser.OpenSubKey(UninstallRoot, writable: true);
        root?.DeleteSubKeyTree(ProductInfo.UninstallKeyName, throwOnMissingSubKey: false);
    }
}
