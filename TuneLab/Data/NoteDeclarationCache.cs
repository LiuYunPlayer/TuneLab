using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// 单 note 声明（note 属性 config + 逐 slot 音素属性 config）的按 note 缓存：context 里只有这一个 note，与侧栏单选同一份口径。
// 参数栏 lane 的每一段都按它所在 note 自己的声明画、写（量程 / 默认值 / 字段在不在），而不是拿整个 part 合并出的一份套到所有 note 上：
// 插件的声明可以随所选 note 的值变化，合并后的形状对单个 note 未必成立。
//
// 失效只能按 part 整体：声明 context 经 Part 能读到整个 part（全部 note、自动化曲线、part 参数，SDK 明文允许据此条件化），
// 任何一处变了都可能改任何一个 note 的回答。失效只清表、不重新询问——用到哪个 note 再声明哪个（视口内的、侧栏选中的）。
internal sealed class NoteDeclarationCache(MidiPart part)
{
    public ObjectConfig NoteConfig(INote note)
    {
        var entry = EntryOf(note);
        return entry.Note ??= part.SoundSource.GetNotePropertyConfig(Context(note));
    }

    public IReadOnlyMap<int, ObjectConfig> PhonemeConfigs(INote note)
    {
        var entry = EntryOf(note);
        return entry.Phonemes ??= part.SoundSource.GetPhonemePropertyConfigs(Context(note));
    }

    // 这个 note 的声明里 lane（路径，见 LanePath）指到的字段：路径对不上（这个 note 没声明它）返回 null。
    public LanePath.Resolved? ResolveNoteLane(INote note, string path)
    {
        var entry = EntryOf(note);
        if (!entry.NoteLanes.TryGetValue(path, out var resolved))
        {
            resolved = LanePath.Resolve(NoteConfig(note), path);
            entry.NoteLanes[path] = resolved;
        }
        return resolved;
    }

    // 第 index 个显示音素（引导 ++ 主体）上 lane 指到的字段：按 slot（位置 − 引导个数，口径见 SDK PhonemeSlots）查这个 note 的音素声明。
    public LanePath.Resolved? ResolvePhonemeLane(INote note, int index, string path)
    {
        var entry = EntryOf(note);
        int slot = index - note.PhonemeLeadCount();
        if (!entry.PhonemeLanes.TryGetValue((slot, path), out var resolved))
        {
            resolved = PhonemeConfigs(note).GetValue(slot, out bool found) is { } slotConfig && found ? LanePath.Resolve(slotConfig, path) : null;
            entry.PhonemeLanes[(slot, path)] = resolved;
        }
        return resolved;
    }

    // 这个 note 在 lane 上的数值形状；字段不在或不是有界数值返回 false（该 note 在这条 lane 上没有段）。
    // Color 留空：轨色跟钉选走，由调用方从 lane 条目取。
    public bool TryGetNoteLaneShape(INote note, string path, out LaneEntry shape)
        => TryGetShape(ResolveNoteLane(note, path), out shape);

    public bool TryGetPhonemeLaneShape(INote note, int index, string path, out LaneEntry shape)
        => TryGetShape(ResolvePhonemeLane(note, index, path), out shape);

    static bool TryGetShape(LanePath.Resolved? resolved, out LaneEntry shape)
    {
        if (resolved != null && LaneEntry.TryGetBoundedNumber(resolved.Leaf, out shape))
            return true;
        shape = default;
        return false;
    }

    public void Invalidate()
    {
        mEntries.Clear();
        mPartContext = null;
    }

    // 只有音素声明过期（合成音素回填：音素声明依赖显示音素，而合成产物不是数据、不经 part 的 Modified）。
    public void InvalidatePhonemes()
    {
        foreach (var entry in mEntries.Values)
        {
            entry.Phonemes = null;
            entry.PhonemeLanes.Clear();
        }
    }

    Entry EntryOf(INote note)
    {
        if (!mEntries.TryGetValue(note, out var entry))
        {
            entry = new();
            mEntries.Add(note, entry);
        }
        return entry;
    }

    // part 视图在同一代缓存里共用：它直读数据层当前值，失效之前数据不变，各 note 的声明读到的是同一份 part。
    NotePropertyContext Context(INote note)
        => new(mPartContext ??= new PartContext(part), [new PartContext.PartNote(note)]);

    sealed class Entry
    {
        public ObjectConfig? Note;
        public IReadOnlyMap<int, ObjectConfig>? Phonemes;
        public readonly Dictionary<string, LanePath.Resolved?> NoteLanes = new();
        public readonly Dictionary<(int Slot, string Path), LanePath.Resolved?> PhonemeLanes = new();
    }

    readonly Dictionary<INote, Entry> mEntries = new();
    PartContext? mPartContext;
}
