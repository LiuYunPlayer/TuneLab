using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TuneLab.Setup.Core;

/// <summary>
/// 在 HKCU\Software\Classes 下把 TuneLab 工程扩展名（.tlp / .tlpx）关联到本程序。
/// 每用户级，无需管理员。卸载时反注册并通知资源管理器刷新。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FileAssociation
{
    const string ProgId = "TuneLab.Project";
    static readonly string[] Extensions = { ".tlp", ".tlpx" };

    public static void Register(string installDir)
    {
        string exePath = Path.Combine(installDir, ProductInfo.ExecutableName);
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");

        // ProgId：显示名 + 图标 + 打开命令
        using (var progId = classes.CreateSubKey(ProgId))
        {
            progId.SetValue(string.Empty, "TuneLab Project");
            using (var icon = progId.CreateSubKey("DefaultIcon"))
                icon.SetValue(string.Empty, $"\"{exePath}\",0");
            using (var cmd = progId.CreateSubKey(@"shell\open\command"))
                cmd.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
        }

        // 各扩展名指向 ProgId
        foreach (var ext in Extensions)
        {
            using var extKey = classes.CreateSubKey(ext);
            extKey.SetValue(string.Empty, ProgId);
        }

        NotifyShell();
    }

    public static void Unregister()
    {
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
        if (classes == null) return;

        foreach (var ext in Extensions)
        {
            // 仅当该扩展名当前指向我们的 ProgId 时才移除，避免误删他人关联。
            using var extKey = classes.OpenSubKey(ext);
            if (extKey?.GetValue(string.Empty) as string == ProgId)
            {
                extKey.Dispose();
                classes.DeleteSubKey(ext, throwOnMissingSubKey: false);
            }
        }
        classes.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);

        NotifyShell();
    }

    static void NotifyShell()
    {
        // SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
