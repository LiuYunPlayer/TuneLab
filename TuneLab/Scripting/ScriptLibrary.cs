using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TuneLab.Foundation;

namespace TuneLab.Scripting;

// 用户脚本库：把 .js 脚本以普通文件存在 TuneLab 管理目录（PathManager.ScriptsFolder = %APPDATA%/TuneLab/Scripts）下，
// 由本类做 list/read/save/delete/rename/import。脚本名 = 文件名去掉 .js 扩展；目录由 TuneLab 创建与维护，
// 用户也可直接往该文件夹丢 .js 文件，下次打开下拉即可见。统一 UTF-8 无 BOM 读写。
internal static class ScriptLibrary
{
    const string Ext = ".js";
    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // 库内全部脚本名（去扩展），按名升序（OrdinalIgnoreCase）。目录不存在或读失败 → 空列表。
    public static List<string> List()
    {
        try
        {
            if (!Directory.Exists(PathManager.ScriptsFolder))
                return [];
            return Directory.EnumerateFiles(PathManager.ScriptsFolder, "*" + Ext)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to list scripts: " + ex);
            return [];
        }
    }

    public static bool Exists(string name)
        => !string.IsNullOrWhiteSpace(name) && File.Exists(PathFor(name));

    public static string Read(string name)
        => File.ReadAllText(PathFor(name));

    public static void Save(string name, string code)
    {
        PathManager.MakeSureExist(PathManager.ScriptsFolder);
        File.WriteAllText(PathFor(name), code ?? string.Empty, Utf8NoBom);
    }

    public static void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path))
            File.Delete(path);
    }

    // 重命名：源不存在或目标同名则无操作；目标已存在（不同名）会被覆盖。
    public static void Rename(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
            return;
        if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
            return;

        var src = PathFor(oldName);
        if (!File.Exists(src))
            return;

        var dst = PathFor(newName);
        if (File.Exists(dst))
            File.Delete(dst);
        File.Move(src, dst);
    }

    // 导入后在库中的脚本名 = 源文件名（去扩展、sanitize）；空则回退 "script"。冲突取舍由调用方决定（不自动加副本）。
    public static string NameForImport(string sourcePath)
    {
        var baseName = SanitizeName(Path.GetFileNameWithoutExtension(sourcePath) ?? "");
        return string.IsNullOrWhiteSpace(baseName) ? "script" : baseName;
    }

    // 从任意路径导入一个 .js 进库（以 NameForImport 为名）。overwrite=false 且同名已存在则抛 IOException。
    public static void Import(string sourcePath, bool overwrite)
    {
        PathManager.MakeSureExist(PathManager.ScriptsFolder);
        File.Copy(sourcePath, PathFor(NameForImport(sourcePath)), overwrite);
    }

    static string PathFor(string name)
        => Path.Combine(PathManager.ScriptsFolder, SanitizeName(name) + Ext);

    // 去掉文件名非法字符（脚本名来自用户输入/外部文件名）；不允许目录分隔符越界。
    public static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
