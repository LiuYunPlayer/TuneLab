using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;


namespace TuneLab.Configs;

// part preset 存储：每 preset 一个文件（Configs\Presets\<名字>.tlpartpreset），文件名即 preset 名——
// 用户可直接在资源管理器里转发/复制/改名 preset，放进文件夹即生效；单个文件损坏只丢一条不毁整库。
// preset 种类由后缀名唯一声明（tl 品牌前缀保证全局辨识度，与 .tlp/.tlx 同族；part=.tlpartpreset，
// 未来 effect 实例/链各有专属后缀），文件内不存 type 字段——两个判据会打架，后缀是唯一权威。
// 名字只活在文件名、不入文件内容（单一权威，杜绝改名后内外分歧）；preset 名经 UI/存储层双重校验
// 保证是合法文件名，故可当文件名用——与插件设置相反（插件 id 不受宿主控制，只能单库文件+内部键）。
// 文件内容 { version, soundSource, properties, automations }，
// version 与工程 CURRENT_VERSION 同号跟随——叶子（soundSource / automations / properties）与 TLP JSON
// 共用序列化件（DataInfoJsonUtils / PropertyJsonUtils）。
internal static class PresetConfigManager
{
    const string FileExtension = ".tlpartpreset";
    static string PresetsFolder => Path.Combine(PathManager.ConfigsFolder, "Presets");

    public static List<PartPreset> LoadPresets()
    {
        var presets = new List<PartPreset>();
        if (!Directory.Exists(PresetsFolder))
            return presets;

        foreach (var path in Directory.EnumerateFiles(PresetsFolder, "*" + FileExtension))
        {
            try
            {
                presets.Add(ReadPresetFile(path));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load preset file: " + path + "\n" + ex);
            }
        }
        presets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return presets;
    }

    public static void SavePreset(PartPreset preset)
    {
        var nameError = GetPresetNameError(preset.Name);
        if (nameError != null)
            throw new ArgumentException(nameError, nameof(preset));

        PathManager.MakeSureExist(PresetsFolder);

        // 同名（大小写不敏感）已有文件即覆盖它（沿用其现有文件名大小写）；未来版本的文件拒绝覆盖。
        var path = FindPresetFile(preset.Name) ?? Path.Combine(PresetsFolder, preset.Name + FileExtension);
        ThrowIfNewerVersion(path);
        File.WriteAllText(path, ToJson(preset).ToString(Formatting.Indented));
    }

    public static void DeletePreset(string presetName)
    {
        var path = FindPresetFile(presetName);
        if (path != null)
            File.Delete(path);
    }

    public static void RenamePreset(string oldPresetName, string newPresetName)
    {
        if (oldPresetName.Equals(newPresetName, StringComparison.OrdinalIgnoreCase))
            return;

        var nameError = GetPresetNameError(newPresetName);
        if (nameError != null)
            throw new ArgumentException(nameError, nameof(newPresetName));

        var sourcePath = FindPresetFile(oldPresetName);
        if (sourcePath == null)
            return;

        var targetPath = FindPresetFile(newPresetName);
        if (targetPath != null)
        {
            ThrowIfNewerVersion(targetPath);
            File.Delete(targetPath);
        }

        File.Move(sourcePath, Path.Combine(PresetsFolder, newPresetName + FileExtension));
    }

    // 文件名即 preset 名，故名字须是合法文件名。按 Windows 超集校验（含保留设备名）且各平台一致——
    // 保证 preset 文件转发到任何平台都能落地。返回英文原因（对话框/异常共用），null = 合法。
    public static string? GetPresetNameError(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Preset name cannot be empty.";

        if (name.Length > 100)
            return "Preset name is too long (max 100 characters).";

        if (name[0] == ' ' || name[^1] == ' ' || name[^1] == '.')
            return "Preset name cannot start or end with a space or end with a dot.";

        foreach (var c in name)
        {
            if (c < 32 || InvalidNameChars.Contains(c))
                return "Preset name cannot contain any of: < > : \" / \\ | ? *";
        }

        var stem = name.Split('.')[0];
        if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
            return "Preset name conflicts with a reserved system name.";

        return null;
    }

    static readonly char[] InvalidNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    static PartPreset ReadPresetFile(string path)
    {
        var content = File.ReadAllText(path);
        if (JToken.Parse(content) is not JObject root)
            throw new FormatException("Preset file root is not an object.");

        var version = (int?)root["version"] ?? 0;
        if (version > TuneLabProject.CURRENT_VERSION)
            throw new FormatException("Unsupported preset file version: " + version);

        var preset = FromJson(root);
        preset.Name = Path.GetFileNameWithoutExtension(path);
        return preset;
    }

    static string? FindPresetFile(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName) || !Directory.Exists(PresetsFolder))
            return null;

        return Directory.EnumerateFiles(PresetsFolder, "*" + FileExtension)
            .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Equals(presetName, StringComparison.OrdinalIgnoreCase));
    }

    // 高版本文件绝不清写（与工程同判据）；读不出版本的坏文件允许覆盖。
    static void ThrowIfNewerVersion(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            if (JToken.Parse(File.ReadAllText(path)) is JObject root
                && ((int?)root["version"] ?? 0) > TuneLabProject.CURRENT_VERSION)
                throw new FormatException("Cannot overwrite preset file of a newer version: " + path);
        }
        catch (JsonReaderException)
        {
        }
    }

    static JObject ToJson(PartPreset preset)
    {
        return new JObject
        {
            ["version"] = TuneLabProject.CURRENT_VERSION,
            ["soundSource"] = DataInfoJsonUtils.ToJson(preset.Source),
            ["properties"] = PropertyJsonUtils.ToJson(preset.Properties),
            ["automations"] = DataInfoJsonUtils.ToJson(preset.Automations),
        };
    }

    static PartPreset FromJson(JObject json)
    {
        var preset = new PartPreset()
        {
            Source = DataInfoJsonUtils.ToSoundSourceInfo(json["soundSource"]),
            Properties = json["properties"] is JObject properties ? PropertyJsonUtils.ToPropertyObject(properties) : PropertyObject.Empty,
        };

        if (json["automations"] is JObject automations)
            DataInfoJsonUtils.ReadAutomations(automations, preset.Automations);

        return preset;
    }

}

// part preset = 声源参数域的快照：音源身份 + part 属性 + automation 默认值。Name 即文件名（不入文件内容）。
// 字段直接用 DataInfo 本体，抓取/应用整对象传递（GetInfo/SetInfo），叶子加字段自动流过、不逐字段抄写。
// 快照是物化的（dense）：Properties 与 Automations 都在抓取时把声明默认值落成显式值——默认值也是声音
// 的一部分，引擎日后改默认值不改变既存 preset 的声音。
// 口径（刻意不入 preset 的部分）：时间轴内容（notes / automation 曲线点 / 分段轨 / effect 链）与 part 状态
// （Gain / Name / Pos）——Automations 里的 Points 恒空，应用端也只消费 DefaultValue。
internal class PartPreset
{
    public string Name { get; set; } = string.Empty;
    public SoundSourceInfo Source { get; set; } = new();
    public PropertyObject Properties { get; set; } = PropertyObject.Empty;
    public Map<string, AutomationInfo> Automations { get; set; } = new();
}
