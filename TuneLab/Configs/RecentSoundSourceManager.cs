using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TuneLab.Foundation;

namespace TuneLab.Configs;

// part 音源选择处「最近使用」的唯一数据源（右键菜单 / part 面板下拉共用）：各 kind 一份列表、最近在前。
// 不是用户可调项，故不放进 Settings（Settings 仅承用户可调项），自带独立 JSON 持久化（与 EditorState 同模式）。
// 上限是固定实现细节、不开放给用户，故为私有 const。改后即时存盘 + 触发 Changed（其是下拉的数据源信号，下拉须订阅刷新）。
internal static class RecentSoundSourceManager
{
    const int MaxCount = 5;

    public static IReadOnlyList<RecentSoundSource> Voices => mVoices;
    public static IReadOnlyList<RecentSoundSource> Instruments => mInstruments;
    public static IActionEvent Changed => mChanged;

    public static void PushVoice(string type, string id) => Push(mVoices, type, id);
    public static void PushInstrument(string type, string id) => Push(mInstruments, type, id);

    // 记录一次选用：同身份去重后置顶、截断到上限、即时存盘并广播变更。
    static void Push(List<RecentSoundSource> list, string type, string id)
    {
        list.RemoveAll(r => r.Type == type && r.ID == id);
        list.Insert(0, new RecentSoundSource { Type = type, ID = id });
        if (list.Count > MaxCount)
            list.RemoveRange(MaxCount, list.Count - MaxCount);
        Save(PathManager.RecentSoundSourcesFilePath);
        mChanged.Invoke();
    }

    public static void Init(string path)
    {
        RecentSoundSourceFile? file = null;
        if (File.Exists(path))
        {
            try
            {
                file = JsonSerializer.Deserialize<RecentSoundSourceFile>(File.OpenRead(path));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to deserialize recent sound sources: " + ex);
            }
        }

        file ??= new();
        mVoices = file.Voices ?? new();
        mInstruments = file.Instruments ?? new();
    }

    public static void Save(string path)
    {
        try
        {
            var content = JsonSerializer.Serialize(new RecentSoundSourceFile()
            {
                Voices = mVoices,
                Instruments = mInstruments,
            }, JsonSerializerOptions);

            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(path, content);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save recent sound sources: " + ex);
        }
    }

    static List<RecentSoundSource> mVoices = new();
    static List<RecentSoundSource> mInstruments = new();
    static readonly ActionEvent mChanged = new();
    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}

// 持久化 DTO：各 kind 一份最近列表。
internal class RecentSoundSourceFile
{
    public List<RecentSoundSource> Voices { get; set; } = new();
    public List<RecentSoundSource> Instruments { get; set; } = new();
}

// 最近使用音源的序列化身份；显示名不入盘（避免改名/换语言后陈旧），用时经 VoicesManager / InstrumentsManager 现取。
internal class RecentSoundSource
{
    public string Type { get; set; } = string.Empty;
    public string ID { get; set; } = string.Empty;
}
