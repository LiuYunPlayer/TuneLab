using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TuneLab.Data;
using TuneLab.Extensions.Effect;
using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.SDK;

namespace TuneLab.Configs;

// 叠到钢琴窗上（PianoOverlay）：用户把画布叠在钢琴窗的音符区上，和当前工具一起操作。
// 画布还是同一块画布，只是放的位置不同（同参数区那一摞，见 docs/canvas-stacking.md）。
// 【能叠的】时间轴 lane 画布（铺满音符区）与时间轴画布（每个音符 / 音素一块）；侧栏画布没有时间轴、只传值的没有控件，都不能叠。
// 【两个视图，两套独立的一摞】音符视图（音符工具）与音高视图（其余工具：画笔 / 锚点 / 固定笔刷 / 颤音）各叠各的——
//   有的画布只在其中一种编辑下有参考意义。每摞里有一个固定层（音符视图 = 音符，音高视图 = 音高线）和用户叠上去的画布，
//   顺序 = 悬浮标签的顺序（越靠左越在上）= 画与收事件的顺序。固定层就是当前工具所在的层：它接住所有按下，
//   它之上的画布先被问、不接的落到工具上；它之下的画布只显示、不收事件（作背景）。固定层只能拖动，不能激活、没有小眼睛。
//   默认：音符视图里音符在最底（画布都在音符上），音高视图里音高线在最上（画布都垫在音高线下）。
// 【一摞】顺序 + 各自开没开（小眼睛）+ 激活的那块。激活 = 临时画到最上、总是显示，不改顺序；撤掉激活就回到原位。激活与小眼睛脱钩（同参数区）。
// 【存法】同标签栏隐藏：界面偏好、跟声源 / 效果器类型走（跨工程生效），自带独立 JSON（Configs/PianoOverlay.json），改后即时存盘。
//   键与 ParameterHiding 同一份（ParameterHiding.PreferenceKey）。画布不再被声明时记录留着、不显示，重新声明即恢复。
//   悬浮标签收起与否也记在这里（两个视图共用）。
internal enum OverlayView { Note, Pitch }

internal static class PianoOverlay
{
    // 一层：叠上去的画布，或固定层（IsAnchor，Key 无意义）。Shown = 小眼睛（激活的那块、固定层总是显示）。
    public readonly record struct Layer(AutomationKey Key, string Name, string Color, bool Shown, bool Active, bool IsAnchor = false);

    // 能叠的一块：键、名字、轨色、属于谁（分组名）。
    public readonly record struct Candidate(AutomationKey Key, string Name, string Color, string Group);

    // 叠放、顺序、小眼睛、激活、收起有变化：钢琴窗重画、悬浮标签更新。
    public static IActionEvent Changed => mChanged;

    public static void Init(string path)
    {
        Store? store = null;
        if (File.Exists(path))
        {
            try
            {
                store = JsonSerializer.Deserialize<Store>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to deserialize piano overlays: " + ex);
            }
        }
        mStore = store ?? new();
        mPath = path;
        // 开发期的单摞格式（只有 Layers / Active）：并进音符视图。
        if (mStore.Note == null && mStore.Layers != null)
            mStore.Note = new Stack { Layers = mStore.Layers, Active = mStore.Active };
        mStore.Layers = null;
        mStore.Active = null;
        mStore.Note = EnsureAnchor(mStore.Note, OverlayView.Note);
        mStore.Pitch = EnsureAnchor(mStore.Pitch, OverlayView.Pitch);
    }

    // 固定层的名字：音符视图 = 音符，音高视图 = 音高。
    public static string AnchorName(OverlayView view) => view == OverlayView.Note ? "Note".Tr(TC.Property) : "Pitch".Tr(TC.Menu);

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

    // 这个视图里的各层（叠着的、此刻还被声明着的画布 + 固定层），按用户排的顺序（= 标签顺序，自下而上）。
    // 叠放时激活的那块另画在最上（见 DrawOrder）。
    public static List<Layer> Layers(IMidiPart part, OverlayView view)
    {
        var stack = StackOf(view);
        var result = new List<Layer>();
        var candidates = new Dictionary<string, Candidate>();
        foreach (var candidate in Candidates(part))
        {
            if (ParameterHiding.PreferenceKey(part, candidate.Key) is { } key)
                candidates.TryAdd(key, candidate);
        }
        foreach (var entry in stack.Layers)
        {
            if (entry.Key == AnchorKey)
            {
                result.Add(new Layer(default, AnchorName(view), string.Empty, true, false, true));
                continue;
            }
            if (!candidates.TryGetValue(entry.Key, out var candidate))
                continue;
            bool isActive = entry.Key == stack.Active;
            result.Add(new Layer(candidate.Key, candidate.Name, candidate.Color, entry.Shown || isActive, isActive));
        }
        return result;
    }

    // 叠放顺序（自下而上）：用户排的顺序，激活的那块挪到最上。
    public static List<Layer> DrawOrder(IMidiPart part, OverlayView view)
    {
        var layers = Layers(part, view);
        int active = layers.FindIndex(layer => layer.Active);
        if (active >= 0)
        {
            var layer = layers[active];
            layers.RemoveAt(active);
            layers.Add(layer);
        }
        return layers;
    }

    public static bool IsOverlaid(IMidiPart part, OverlayView view, AutomationKey key) => Find(part, view, key, out _) >= 0;

    // 叠上去 / 取下来（终态、幂等）。叠上去的开着小眼睛，放在默认那一侧的最上：音符视图 = 整摞最上（标签最左），
    // 音高视图 = 紧贴在音高线下面（仍垫在音高线下）。返回是否真的动了。
    public static bool SetOverlaid(IMidiPart part, OverlayView view, AutomationKey key, bool overlaid)
    {
        if (ParameterHiding.PreferenceKey(part, key) is not { } preferenceKey)
            return false;
        var stack = StackOf(view);
        int index = Find(part, view, key, out _);
        if ((index >= 0) == overlaid)
            return false;

        if (overlaid)
        {
            var entry = new Entry { Key = preferenceKey, Shown = true };
            if (view == OverlayView.Note)
                stack.Layers.Add(entry);
            else
                stack.Layers.Insert(stack.Layers.FindIndex(e => e.Key == AnchorKey), entry);
        }
        else
        {
            stack.Layers.RemoveAt(index);
            if (stack.Active == preferenceKey)
                stack.Active = null;
        }
        Save();
        return true;
    }

    // 小眼睛：不激活时显不显示（关了不画、不收事件，但还叠着）。
    public static void SetShown(IMidiPart part, OverlayView view, AutomationKey key, bool shown)
    {
        var stack = StackOf(view);
        int index = Find(part, view, key, out _);
        if (index < 0 || stack.Layers[index].Shown == shown)
            return;
        stack.Layers[index].Shown = shown;
        Save();
    }

    // 激活（点它的标签）：临时画在最上、总是显示；active = false 撤掉激活。不改顺序，也不碰小眼睛。
    public static void SetActive(IMidiPart part, OverlayView view, AutomationKey key, bool active)
    {
        var stack = StackOf(view);
        int index = Find(part, view, key, out var preferenceKey);
        if (index < 0)
            return;
        if (active)
            stack.Active = preferenceKey;
        else if (stack.Active == preferenceKey)
            stack.Active = null;
        else
            return;
        Save();
    }

    // 拖标签排序：order = 此刻显示着的那些层的新顺序（自下而上；null = 固定层）。它们在存储里占的那几个位置按新顺序重新填，
    // 没在显示的（这个声源此刻没声明的）留在原位。
    public static void Reorder(IMidiPart part, OverlayView view, IReadOnlyList<AutomationKey?> order)
    {
        var stack = StackOf(view);
        var slots = new List<int>();
        var entries = new List<Entry>();
        foreach (var key in order)
        {
            int index = key is { } k ? Find(part, view, k, out _) : stack.Layers.FindIndex(e => e.Key == AnchorKey);
            if (index < 0)
                return;
            slots.Add(index);
            entries.Add(stack.Layers[index]);
        }
        slots.Sort();
        bool changed = false;
        for (int i = 0; i < slots.Count; i++)
        {
            if (!ReferenceEquals(stack.Layers[slots[i]], entries[i]))
            {
                stack.Layers[slots[i]] = entries[i];
                changed = true;
            }
        }
        if (changed)
            Save();
    }

    // 悬浮标签收起了没有（收起后只剩展开按钮）。
    public static bool IsCollapsed
    {
        get => mStore.Collapsed;
        set
        {
            if (mStore.Collapsed == value)
                return;
            mStore.Collapsed = value;
            Save();
        }
    }

    static int Find(IMidiPart part, OverlayView view, AutomationKey key, out string? preferenceKey)
    {
        preferenceKey = ParameterHiding.PreferenceKey(part, key);
        if (preferenceKey == null)
            return -1;
        var layers = StackOf(view).Layers;
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i].Key == preferenceKey)
                return i;
        }
        return -1;
    }

    static Stack StackOf(OverlayView view) => view == OverlayView.Note ? mStore.Note! : mStore.Pitch!;

    // 每摞恰好一个固定层：没有就按默认位置补上（音符视图在最底，音高视图在最上），多了只留第一个。
    static Stack EnsureAnchor(Stack? stack, OverlayView view)
    {
        stack ??= new();
        int first = stack.Layers.FindIndex(e => e.Key == AnchorKey);
        if (first < 0)
        {
            var anchor = new Entry { Key = AnchorKey };
            if (view == OverlayView.Note)
                stack.Layers.Insert(0, anchor);
            else
                stack.Layers.Add(anchor);
        }
        else
        {
            for (int i = stack.Layers.Count - 1; i > first; i--)
            {
                if (stack.Layers[i].Key == AnchorKey)
                    stack.Layers.RemoveAt(i);
            }
        }
        return stack;
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
            File.WriteAllText(mPath, JsonSerializer.Serialize(mStore, JsonSerializerOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save piano overlays: " + ex);
        }
    }

    // 存储：两个视图各一摞 + 悬浮标签收起没有。Layers / Active 是开发期的单摞格式，只在读入时并进音符视图。
    sealed class Store
    {
        public Stack? Note { get; set; }
        public Stack? Pitch { get; set; }
        public bool Collapsed { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Entry>? Layers { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Active { get; set; }
    }

    // 一摞：自下而上的条目（含一个固定层）+ 激活的那块（它的键；没有 = null）。
    sealed class Stack
    {
        public List<Entry> Layers { get; set; } = [];
        public string? Active { get; set; }
    }

    sealed class Entry
    {
        public string Key { get; set; } = string.Empty;
        public bool Shown { get; set; } = true;
    }

    // 固定层在存储里的键（不会与画布的键撞：画布键都带声源 / 效果器前缀）。
    const string AnchorKey = "#anchor";

    static Store mStore = new();
    static string mPath = string.Empty;
    static readonly ActionEvent mChanged = new();
    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
