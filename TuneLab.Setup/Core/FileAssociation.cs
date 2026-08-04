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
    /// <summary>
    /// 一条关联：扩展名、ProgId、资源管理器"类型"列显示名、图标文件（相对安装目录）。
    /// </summary>
    readonly record struct Association(string Extension, string ProgId, string DisplayName, string IconPath);

    // .tlpx 与 .tlp 是同一族工程文件的两种编码（前者紧凑二进制、是默认另存格式；后者 JSON 文本）。
    // 每个后缀一个 ProgId 而不是共用一个：DefaultIcon 挂在 ProgId 上，共用就只能共用一枚图标。
    // .tlx 是扩展包，双击即安装（App.HandleArg 按后缀分流到 InstallExtensions）。
    static readonly Association[] Associations =
    {
        new(".tlpx", "TuneLab.Project.Tlpx", "TuneLab Project File",
            @"Assets\FileIcons\TuneLabProject.ico"),
        new(".tlp", "TuneLab.Project.Tlp", "TuneLab Project File (JSON)",
            @"Assets\FileIcons\TuneLabProjectJson.ico"),
        new(".tlx", "TuneLab.Extension", "TuneLab Extension Package",
            @"Assets\FileIcons\TuneLabExtension.ico"),
    };

    // 我们自己历史上用过、现已弃用的 ProgId。
    const string ProgIdPrefix = "TuneLab.";
    static readonly string[] RetiredProgIds = { "TuneLab.Project" };

    const string FileExtsPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

    public static void Register(string installDir)
    {
        string exePath = Path.Combine(installDir, ProductInfo.ExecutableName);
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");

        ClearOwnStaleRecords(classes);

        foreach (var assoc in Associations)
        {
            // ProgId：显示名 + 图标 + 打开命令。图标是随程序部署的独立 ico（不再是 exe 的图标），
            // 这样资源管理器里工程文件与应用本身一眼可分。
            using (var progId = classes.CreateSubKey(assoc.ProgId))
            {
                progId.SetValue(string.Empty, assoc.DisplayName);
                using (var icon = progId.CreateSubKey("DefaultIcon"))
                    icon.SetValue(string.Empty, $"\"{Path.Combine(installDir, assoc.IconPath)}\",0");
                using (var cmd = progId.CreateSubKey(@"shell\open\command"))
                    cmd.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
            }

            using var extKey = classes.CreateSubKey(assoc.Extension);
            extKey.SetValue(string.Empty, assoc.ProgId);
        }

        NotifyShell();
    }

    public static void Unregister()
    {
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
        if (classes == null) return;

        foreach (var assoc in Associations)
        {
            // 仅当该扩展名当前指向我们的 ProgId 时才移除，避免误删他人关联。
            using (var extKey = classes.OpenSubKey(assoc.Extension))
            {
                if (extKey?.GetValue(string.Empty) as string == assoc.ProgId)
                {
                    extKey.Dispose();
                    classes.DeleteSubKey(assoc.Extension, throwOnMissingSubKey: false);
                }
            }
            classes.DeleteSubKeyTree(assoc.ProgId, throwOnMissingSubKey: false);
        }
        ClearOwnStaleRecords(classes);

        NotifyShell();
    }

    /// <summary>
    /// 清掉我们自己留下的历史痕迹：弃用的 ProgId，以及 shell 在
    /// FileExts\&lt;ext&gt;\OpenWithProgids 下记着的、指向我们 ProgId 的条目。
    ///
    /// 为什么必须清：一旦我们改过 ProgId 名（1.x 时三个后缀共用 TuneLab.Project，
    /// 2.0 拆成每后缀一个），shell 那份旧记录就与 Classes\&lt;ext&gt; 里的新名字对不上，
    /// 于是它既取不到显示名也取不到图标——「类型」列退化成显示 ProgId 字符串本身、
    /// 图标退化成通用文档（实测如此，删掉旧记录立刻恢复）。覆盖安装不会自动清它。
    ///
    /// 只删前缀是 TuneLab. 的条目：同一个后缀下可能还有用户装的其它程序的记录，不碰。
    /// </summary>
    static void ClearOwnStaleRecords(RegistryKey classes)
    {
        foreach (var progId in RetiredProgIds)
            classes.DeleteSubKeyTree(progId, throwOnMissingSubKey: false);

        foreach (var assoc in Associations)
        {
            using var openWith = Registry.CurrentUser.OpenSubKey(
                $@"{FileExtsPath}\{assoc.Extension}\OpenWithProgids", writable: true);
            if (openWith == null)
                continue;

            foreach (var name in openWith.GetValueNames())
            {
                if (name.StartsWith(ProgIdPrefix, StringComparison.OrdinalIgnoreCase))
                    openWith.DeleteValue(name, throwOnMissingValue: false);
            }
        }
    }

    static void NotifyShell()
    {
        // SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
