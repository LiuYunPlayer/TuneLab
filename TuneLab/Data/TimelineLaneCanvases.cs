using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// 一条时间轴 lane 画布的存储：插件的整值 + 值的原点在 part 里的位置（相对 part 原点的 tick，见 TimelineLaneCanvasInfo.Offset）。
internal readonly record struct TimelineLaneCanvasEntry(double Offset, PropertyValue Value);

// 一段改动范围：part 局部 tick（相对 part 原点，已含偏移），[Start, End)。
internal readonly record struct TickRange(double Start, double End);

// part / 效果器上的时间轴 lane 画布表（id → 偏移 + 整值）。整块存：一次写入 = 换掉一个条目的引用，撤销 = 换回来，不拆成数据节点树
//（值宿主不解读，拆开没有用处，大值时拆树的开销不可接受）。不看当前声明：没被声明的 id（孤儿）原样存着。
// 每次写入（含撤销 / 重做）在落值后触发 CanvasModified(id, 改动范围)：范围是写入时报的那几段（part 局部 tick），null = 整条都算改了。
internal sealed class TimelineLaneCanvases : DataObject
{
    public IActionEvent<string, IReadOnlyList<TickRange>?> CanvasModified => mCanvasModified;
    public IEnumerable<IReadOnlyKeyValuePair<string, TimelineLaneCanvasEntry>> Items => mEntries;
    public int Count => mEntries.Count;

    public TimelineLaneCanvases(DataObject parent) : base(parent) { }

    public bool TryGetValue(string id, out TimelineLaneCanvasEntry entry) => mEntries.TryGetValue(id, out entry);

    // 读值：没设过 = Null。
    public PropertyValue GetValue(string id) => mEntries.TryGetValue(id, out var entry) ? entry.Value : PropertyValue.Null;

    // 只读值视图（声明视图 / 合成上下文用），按 id；没设过的不在表里。
    public IReadOnlyMap<string, PropertyValue> GetValues()
    {
        var map = new Map<string, PropertyValue>();
        foreach (var kvp in mEntries)
            map.Add(kvp.Key, kvp.Value.Value);
        return map;
    }

    // 写一个条目的值（Null = 删掉存值，即"没设过"），偏移沿用已有条目的（新条目 0）。ranges 按值的局部 tick（插件看到的 tick），
    // 这里换成 part 局部 tick 随命令记下；null = 整条。值没变就什么都不做。
    public void SetValue(string id, PropertyValue value, IReadOnlyList<TickRange>? ranges = null)
    {
        mEntries.TryGetValue(id, out var before);
        bool hadBefore = mEntries.ContainsKey(id);
        TimelineLaneCanvasEntry? after = value.IsNull() ? null : new TimelineLaneCanvasEntry(hadBefore ? before.Offset : 0, value);
        TimelineLaneCanvasEntry? previous = hadBefore ? before : null;
        if (Equals(previous, after))
            return;

        IReadOnlyList<TickRange>? partRanges = null;
        if (ranges != null)
        {
            double offset = hadBefore ? before.Offset : 0;
            var list = new List<TickRange>(ranges.Count);
            foreach (var range in ranges)
                list.Add(new TickRange(range.Start + offset, range.End + offset));
            partRanges = list;
        }
        PushAndDo(new ModifyCommand(this, id, previous, after, partRanges));
    }

    // 整条换掉一个条目（偏移 + 值；null = 删掉），按整条通知。拆分 / 合并 / 预设等宿主发起的写入用。
    public void SetEntry(string id, TimelineLaneCanvasEntry? entry)
    {
        TimelineLaneCanvasEntry? previous = mEntries.TryGetValue(id, out var before) ? before : null;
        if (entry is { } e && e.Value.IsNull())
            entry = null;
        if (Equals(previous, entry))
            return;

        PushAndDo(new ModifyCommand(this, id, previous, entry, null));
    }

    public Map<string, TimelineLaneCanvasInfo> GetInfo()
    {
        var map = new Map<string, TimelineLaneCanvasInfo>();
        foreach (var kvp in mEntries)
            map.Add(kvp.Key, new TimelineLaneCanvasInfo { Offset = kvp.Value.Offset, Value = kvp.Value.Value });
        return map;
    }

    // 整表换成 info（装载、撤销整个 part 的 SetInfo）：逐 id 比较，只写变了的条目，各按整条通知。
    public void SetInfo(IReadOnlyMap<string, TimelineLaneCanvasInfo> info)
    {
        var removed = new List<string>();
        foreach (var kvp in mEntries)
        {
            if (!info.ContainsKey(kvp.Key))
                removed.Add(kvp.Key);
        }
        foreach (var id in removed)
            SetEntry(id, null);
        foreach (var kvp in info)
            SetEntry(kvp.Key, new TimelineLaneCanvasEntry(kvp.Value.Offset, kvp.Value.Value));
    }

    void Write(string id, TimelineLaneCanvasEntry? entry, IReadOnlyList<TickRange>? ranges)
    {
        NotifyWill();
        if (entry is { } e)
            mEntries[id] = e;
        else
            mEntries.Remove(id);
        Notify();
        mCanvasModified.Invoke(id, ranges);
    }

    sealed class ModifyCommand(TimelineLaneCanvases owner, string id, TimelineLaneCanvasEntry? before, TimelineLaneCanvasEntry? after, IReadOnlyList<TickRange>? ranges) : IDataCommand
    {
        public void Redo() => owner.Write(id, after, ranges);
        public void Undo() => owner.Write(id, before, ranges);
    }

    // 保持插入序（= 首次写入序），写工程时稳定。
    readonly OrderedMap<string, TimelineLaneCanvasEntry> mEntries = new();
    readonly ActionEvent<string, IReadOnlyList<TickRange>?> mCanvasModified = new();
}
