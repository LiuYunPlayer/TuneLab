using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TuneLab.Setup.Core;

// 读写主程序用户设置里的界面语言（%AppData%/TuneLab/Configs/Settings.json，与主程序共用同一份文件）。
// 安装器据此决定初始界面语言，并把用户在安装器里改的语言写回，让主程序首启即用该语言。
internal static class UserSettings
{
    static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuneLab", "Configs");
    static string SettingsPath => Path.Combine(ConfigDir, "Settings.json");

    // 读已保存的界面语言；无文件/无字段/空值/解析失败均返回 null（交由调用方回退英文）。
    public static string? ReadLanguage()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return null;
            var lang = JsonNode.Parse(File.ReadAllText(SettingsPath))?["Language"]?.GetValue<string>();
            return string.IsNullOrEmpty(lang) ? null : lang;
        }
        catch
        {
            return null;
        }
    }

    // 把语言写回设置文件，保留其余字段（读-改-写）；文件不存在则新建仅含该字段的最小设置
    // （主程序反序列化时缺失字段走默认值，故最小文件安全）。写回属锦上添花，失败静默。
    public static void WriteLanguage(string language)
    {
        try
        {
            if (string.IsNullOrEmpty(language))
                return;

            JsonObject obj = File.Exists(SettingsPath)
                && JsonNode.Parse(File.ReadAllText(SettingsPath)) is JsonObject existing
                ? existing
                : new JsonObject();

            obj["Language"] = language;
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(SettingsPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}
