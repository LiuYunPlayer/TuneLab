using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TuneLab.Setup.Core;

/// <summary>
/// 卸载的删除范围。<see cref="Delete"/> 是清单认领过、此刻确实躺在安装目录里的文件；
/// <see cref="Keep"/> 是目录里但没人认领的——不是安装器放的，一件都不动，只如实报出来。
/// </summary>
internal readonly record struct UninstallPlan(IReadOnlyList<string> Delete, IReadOnlyList<string> Keep);

/// <summary>
/// 安装清单：安装器把「这次往安装目录里放了哪些文件」记成安装目录下的一份文本，卸载照着它删。
///
/// 【为什么要有它】安装目录是调用方给的（<c>-silent -dir D:\Apps</c> 就把 D:\Apps 整个交了出来，
/// 那里往往还有别的东西）。没有清单，卸载只能对着这个目录做一次递归删除，删掉的就不止是我们装的。
/// 有了清单，删除范围从"某个目录"变成"我们自己铺过的那些文件"——用户往那个目录里放了什么与卸载无关。
///
/// 【为什么它天然越不出安装目录】要删哪些是清单与「枚举安装目录得到的相对路径」的交集，而枚举出来的
/// 路径必定落在目录内，所以一份被人改坏的清单（写上 <c>..\..\Windows</c>）什么也匹配不上。
/// 解析时仍会把这类条目挡掉，是为了让"清单只能描述目录内的文件"这件事在格式层面就成立。
/// </summary>
internal static class InstallManifest
{
    public const string FileName = "install-manifest.txt";

    /// <summary>清单格式版本。</summary>
    const int CurrentVersion = 1;
    const string VersionPrefix = "version ";

    // ---- 格式（纯函数，可单测） ----

    public static string Format(IEnumerable<string> entries)
    {
        var sb = new StringBuilder();
        sb.Append("# TuneLab install manifest: the files the installer put in this folder.\r\n");
        sb.Append("# Uninstalling deletes exactly these and leaves everything else alone.\r\n");
        sb.Append("# Rewritten on every install and update. Editing it changes what uninstall removes.\r\n");
        sb.Append(VersionPrefix).Append(CurrentVersion).Append("\r\n");

        // 排序 + 去重：清单是要被人读、被 diff 的，顺序不该随文件系统的枚举次序变。
        foreach (var entry in entries.Select(Normalize).Where(IsContained)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
            sb.Append(entry).Append("\r\n");

        return sb.ToString();
    }

    /// <summary>解析清单。不是一份认得出的清单时返回 null——调用方据此走"没有清单"那条路。</summary>
    public static IReadOnlyList<string>? Parse(string text)
    {
        var entries = new List<string>();
        bool versioned = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line.StartsWith(VersionPrefix, StringComparison.Ordinal))
            {
                // 读到更高的版本就整份不解释：这是一张删除清单，"大概能看懂"不够——新版本可能
                // 给某些行赋了别的含义，照着旧规矩删就是删错东西。
                if (!int.TryParse(line[VersionPrefix.Length..], out var version) || version > CurrentVersion)
                    return null;
                versioned = true;
                continue;
            }

            var entry = Normalize(line);
            if (IsContained(entry))
                entries.Add(entry);
        }

        // 没有版本行 = 不是这个格式的文件，别拿它当清单用。
        return versioned ? entries : null;
    }

    // ---- 落盘 ----

    public static void Write(string installDir, IEnumerable<string> entries)
        => File.WriteAllText(Path.Combine(installDir, FileName), Format(entries), new UTF8Encoding(false));

    /// <summary>读安装目录里的清单。文件不在、读不动或格式认不出，都返回 null。</summary>
    public static IReadOnlyList<string>? Read(string installDir)
    {
        var path = Path.Combine(installDir, FileName);
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---- 决策（纯函数，可单测） ----

    /// <summary>清单与目录现状取交集：认领得到的删，认领不到的留。</summary>
    public static UninstallPlan Plan(IEnumerable<string> manifest, IEnumerable<string> onDisk)
    {
        var claimed = new HashSet<string>(manifest.Select(Normalize), StringComparer.OrdinalIgnoreCase);

        var delete = new List<string>();
        var keep = new List<string>();
        foreach (var file in onDisk)
            (claimed.Contains(Normalize(file)) ? delete : keep).Add(file);

        return new UninstallPlan(delete, keep);
    }

    /// <summary>
    /// 没有清单时（旧安装器装的那一份）还能不能整目录删：只有目录名就是产品名才行。
    /// 那样的目录是安装器自己造出来的，删它碰不到别人的东西；用户随手指的目录（<c>-dir D:\Apps</c>）
    /// 则不猜。
    /// </summary>
    public static bool MayDeleteWholeDirectoryWithoutManifest(string installDir)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir)));
        return string.Equals(name, ProductInfo.ProductName, StringComparison.OrdinalIgnoreCase);
    }

    // 清单里写的是相对路径。分隔符统一成 Windows 的，比较一律忽略大小写（NTFS 如此）。
    static string Normalize(string relativePath)
        => relativePath.Trim().Replace('/', '\\');

    // "描述的是安装目录之内的一个文件"——绝对路径、盘符、`..` 一律不是。
    static bool IsContained(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return false;

        return !relativePath.Split('\\').Any(segment => segment is ".." or ".");
    }
}
