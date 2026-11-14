using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TuneLab.Data;
using TuneLab.Extensions.Effect;
using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.SDK;

namespace TuneLab.Configs;

// 叠到钢琴窗上（PianoOverlay）：用户把画布叠在钢琴窗的音符区上，和当前工具一起操作（不分工具，切工具不变）。
// 画布还是同一块画布，只是放的位置不同（同参数区那一摞，见 docs/canvas-stacking.md）。
// 【能叠的】时间轴 lane 画布（铺满音符区）与时间轴画布（每个音符 / 音素一块）；侧栏画布没有时间轴、只传值的没有控件，都不能叠。
// 【一摞】用户排的顺序（= 悬浮标签的顺序，越靠左越在上）+ 各自开没开（小眼睛）+ 激活的那块。
//   激活 = 临时画到最上、总是显示，不改顺序；撤掉激活就回到它在顺序里的位置。激活与小眼睛脱钩（同参数区）。
// 【存法】同标签栏隐藏：界面偏好、跟声源 / 效果器类型走（跨工程生效），自带独立 JSON（Configs/PianoOverlay.json），改后即时存盘。
//   键与 ParameterHiding 同一份（ParameterHiding.PreferenceKey）。画布不再被声明时记录留着、不显示，重新声明即恢复。
//   悬浮标签收起与否也记在这里。
internal static class PianoOverlay
{
    // 一块叠上去的画布。Shown = 小眼睛（激活的那块总是显示）。
    public readonly record struct Layer(AutomationKey Key, string Name, string Color, bool Shown, bool Active);

    // 能叠的一块：键、名字、轨色、属于谁（分组名）。
    public readonly record struct Candidate(AutomationKey Key, string Name, string Color, string Group);

    // 叠放、顺序、小眼睛、激活、收起有变化：钢琴窗重画、悬浮标签更新。
    public static IActionEvent Changed => mChanged;

    public static void Init(string path)
    {
        Stack? stack = null;
        if (File.Exists(path))
        {
            try
            {
                stack = JsonSerializer.Deserialize<Stack>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to deserialize piano overlays: " + ex);
            }
        }
        mStack = stack ?? new();
        mPath = path;
    }

    // 这个 part 此刻能叠的全部画布，按标签栏的分组与顺序（声源、音符、音素、各效果器）。
    public static List<Candidate> Candidates(IMidiPart part)
    {
        var result = new List<Candidate>();
        void Add(AutomationKey key, PropertyKey name, string color, string group)
        {
            if (!ParameterHiding.IsValueOnly(part, key))
                result.Add(new(key, name.DisplayText ?? name.Id, color, group));
        }

        var sourceName = string.IsNullOrEmpty(part.SoundSource.Name) ? part.SoundSource.Type : part.SoundSource.Name;
        foreach (var kvp in part.SoundSource.TimelineLaneCanvasConfigs)
        {
            if (kvp.Value.CreateControl != null)
                Add(AutomationKey.VoiceCanvas(kvp.Key.Id), kvp.Key, kvp.Value.Color, sourceName);
        }
        foreach (var kvp in part.NoteLaneConfigs)
        {
            if (kvp.Value.Kind == LaneKind.TimelineCanvas)
                Add(AutomationKey.NoteLane(kvp.Key.Id), kvp.Key, kvp.Value.Color, "Note".Tr(TC.Property));
        }
        foreach (var kvp in part.PhonemeLaneConfigs)
        {
            if (kvp.Value.Kind == LaneKind.TimelineCanvas)
                Add(AutomationKey.PhonemeLane(kvp.Key.Id), kvp.Key, kvp.Value.Color, "Phoneme".Tr(TC.Property));
        }
        for (int i = 0; i < part.Effects.Count; i++)
        {
            var group = EffectManager.GetDisplayName(part.Effects[i].Type);
            foreach (var kvp in part.Effects[i].TimelineLaneCanvasConfigs)
            {
                if (kvp.Value.CreateControl != null)
                    Add(AutomationKey.EffectCanvas(i, kvp.Key.Id), kvp.Key, kvp.Value.Color, group);
            }
        }
        return result;
    }

    // 叠着的、此刻还被声明着的画布，按用户排的顺序（= 标签顺序，自下而上）。叠放时激活的那块另画在最上（见 DrawOrder）。
    public static List<Layer> Layers(IMidiPart part)
    {
        var result = new List<Layer>();
        var candidates = new Dictionary<string, Candidate>();
        foreach (var candidate in Candidates(part))
        {
            if (ParameterHiding.PreferenceKey(part, candidate.Key) is { } key)
                candidates.TryAdd(key, candidate);
        }
        foreach (var entry in mStack.Layers)
        {
            if (!candidates.TryGetValue(entry.Key, out var candidate))
                continue;
            bool isActive = entry.Key == mStack.Active;
            result.Add(new Layer(candidate.Key, candidate.Name, candidate.Color, entry.Shown || isActive, isActive));
        }
        return result;
    }

    // 叠放顺序（自下而上）：用户排的顺序，激活的那块挪到最上。
    public static List<Layer> DrawOrder(IMidiPart part)
    {
        var layers = Layers(part);
        int active = layers.FindIndex(layer => layer.Active);
        if (active >= 0)
        {
            var layer = layers[active];
            layers.RemoveAt(active);
            layers.Add(layer);
        }
        return layers;
    }

    public static bool IsOverlaid(IMidiPart part, AutomationKey key) => Find(part, key, out _) >= 0;

    // 叠上去 / 取下来（终态、幂等）。叠上去的放在最上（标签最左）、开着小眼睛。返回是否真的动了。
    public static bool SetOverlaid(IMidiPart part, AutomationKey key, bool overlaid)
    {
        if (ParameterHiding.PreferenceKey(part, key) is not { } preferenceKey)
            return false;
        int index = Find(part, key, out _);
        if ((index >= 0) == overlaid)
            return false;

        if (overlaid)
        {
            mStack.Layers.Add(new Entry { Key = preferenceKey, Shown = true });
        }
        else
        {
            mStack.Layers.RemoveAt(index);
            if (mStack.Active == preferenceKey)
                mStack.Active = null;
        }
        Save();
        return true;
    }

    // 小眼睛：不激活时显不显示（关了不画、不收事件，但还叠着）。
    public static void SetShown(IMidiPart part, AutomationKey key, bool shown)
    {
        int index = Find(part, key, out _);
        if (index < 0 || mStack.Layers[index].Shown == shown)
            return;
        mStack.Layers[index].Shown = shown;
        Save();
    }

    // 激活（点它的标签）：临时画在最上、总是显示；active = false 撤掉激活。不改顺序，也不碰小眼睛。
    public static void SetActive(IMidiPart part, AutomationKey key, bool active)
    {
        int index = Find(part, key, out var preferenceKey);
        if (index < 0)
            return;
        if (active)
            mStack.Active = preferenceKey;
        else if (mStack.Active == preferenceKey)
            mStack.Active = null;
        else
            return;
        Save();
    }

    // 拖标签排序：order = 此刻显示着的那些画布的新顺序（自下而上）。它们在存储里占的那几个位置按新顺序重新填，
    // 没在显示的（这个声源此刻没声明的）留在原位。
    public static void Reorder(IMidiPart part, IReadOnlyList<AutomationKey> order)
    {
        var slots = new List<int>();
        var entries = new List<Entry>();
        foreach (var key in order)
        {
            int index = Find(part, key, out _);
            if (index < 0)
                return;
            slots.Add(index);
            entries.Add(mStack.Layers[index]);
        }
        slots.Sort();
        bool changed = false;
        for (int i = 0; i < slots.Count; i++)
        {
            if (!ReferenceEquals(mStack.Layers[slots[i]], entries[i]))
            {
                mStack.Layers[slots[i]] = entries[i];
                changed = true;
            }
        }
        if (changed)
            Save();
    }

    // 悬浮标签收起了没有（收起后只剩展开按钮）。
    public static bool IsCollapsed
    {
        get => mStack.Collapsed;
        set
        {
            if (mStack.Collapsed == value)
                return;
            mStack.Collapsed = value;
            Save();
        }
    }

    static int Find(IMidiPart part, AutomationKey key, out string? preferenceKey)
    {
        preferenceKey = ParameterHiding.PreferenceKey(part, key);
        if (preferenceKey == null)
            return -1;
        for (int i = 0; i < mStack.Layers.Count; i++)
        {
            if (mStack.Layers[i].Key == preferenceKey)
                return i;
        }
        return -1;
    }

    static void Save()
    {
        mChanged.Invoke();
        if (string.IsNullOrEmpty(mPath))
            return;

        try
        {
            var folder = Path.GetDirectoryName(mPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(mPath, JsonSerializer.Serialize(mStack, JsonSerializerOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save piano overlays: " + ex);
        }
    }

    // 那一摞：自下而上的条目 + 激活的那块（它的键；没有 = null）+ 悬浮标签收起没有。
    sealed class Stack
    {
        public List<Entry> Layers { get; set; } = [];
        public string? Active { get; set; }
        public bool Collapsed { get; set; }
    }

    sealed class Entry
    {
        public string Key { get; set; } = string.Empty;
        public bool Shown { get; set; } = true;
    }

    static Stack mStack = new();
    static string mPath = string.Empty;
    static readonly ActionEvent mChanged = new();
    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
