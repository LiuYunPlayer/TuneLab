using System.Collections.Generic;

namespace TuneLab.Foundation;

// 把多个属性快照合成一个三态快照的公共工具——多选属性 config 求值的便利路径：
// 不在乎多选的插件 `PropertyMerge.Merge(context.NoteProperties)` 即把列表还原成单个三态 PropertyObject，
// 按单选写法处理；需要逐成员真值的插件直接遍历列表，不调本helper。
//
// 对齐规则（随身份模型，与宿主 live 绑定侧的 MultipleDataPropertyObject / MultipleDataPropertyArray 一致）：
//   标量——各成员同值给该值、不等给 Multiple；
//   数组——按 index 对齐，长度取最长成员，缺位逐项给 Multiple；
//   对象——按 key 并集对齐，逐键递归。
// 容器型即使部分成员缺该键也按结构合并（缺席当空容器，保住长度/键并集），仅标量缺席整体 Multiple；递归触底。
public static class PropertyMerge
{
    public static PropertyObject Merge(IReadOnlyList<PropertyObject> snapshots)
    {
        if (snapshots.Count == 0)
            return PropertyObject.Empty;
        if (snapshots.Count == 1)
            return snapshots[0];

        var slots = new Slot[snapshots.Count];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = Slot.Of(PropertyValue.Create(snapshots[i]));

        return MergeValues(slots).ToObject(out var merged) ? merged : PropertyObject.Empty;
    }

    // 一个成员在某位/键上的参与项：缺席（短于该 index / 无该 key）以 Present=false 参与。
    readonly struct Slot
    {
        public readonly bool Present;
        public readonly PropertyValue Value;
        Slot(bool present, PropertyValue value) { Present = present; Value = value; }
        public static Slot Of(PropertyValue value) => new(true, value);
        public static readonly Slot Absent = new(false, PropertyValue.Null);
    }

    static PropertyValue MergeValues(IReadOnlyList<Slot> slots)
    {
        // 取 present 成员的主类型。容器型（数组/对象）即使部分成员缺席也按结构合并——缺席当空容器，
        // 长度取最长 / 键取并集（保住「该显示几行 / 哪些键」），缺位逐项落 Multiple；
        // 标量型只要有成员缺席即整体 Multiple（标量缺席=值不同）。
        PropertyType? type = null;
        bool anyAbsent = false;
        bool mixed = false;
        foreach (var slot in slots)
        {
            if (!slot.Present) { anyAbsent = true; continue; }
            if (type == null) type = slot.Value.Type;
            else if (slot.Value.Type != type) mixed = true;
        }
        if (type == null) return PropertyValue.Multiple;   // 全缺席（不应发生：至少一个成员有该键）
        if (mixed) return PropertyValue.Multiple;           // present 成员类型不一

        if (type == PropertyType.Array)
            return MergeArrays(slots);
        if (type == PropertyType.Object)
            return MergeObjects(slots);

        // 标量 / null：任一缺席即差异；否则逐成员深比较。
        if (anyAbsent)
            return PropertyValue.Multiple;
        var first = slots[0].Value;
        for (int i = 1; i < slots.Count; i++)
            if (!slots[i].Value.Equals(first))
                return PropertyValue.Multiple;
        return first;
    }

    static PropertyValue MergeArrays(IReadOnlyList<Slot> slots)
    {
        var arrays = new PropertyArray[slots.Count];
        int maxLen = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            // 缺席（或非数组）成员当空数组：长度取 present 成员里最长，缺位逐项落 Multiple。
            arrays[i] = slots[i].Present && slots[i].Value.ToArray(out var array) ? array : PropertyArray.Empty;
            if (arrays[i].Count > maxLen)
                maxLen = arrays[i].Count;
        }

        var result = new List<PropertyValue>(maxLen);
        var childSlots = new Slot[slots.Count];
        for (int index = 0; index < maxLen; index++)
        {
            for (int i = 0; i < arrays.Length; i++)
                childSlots[i] = index < arrays[i].Count ? Slot.Of(arrays[i][index]) : Slot.Absent;
            result.Add(MergeValues(childSlots));
        }
        return new PropertyArray(result);
    }

    static PropertyValue MergeObjects(IReadOnlyList<Slot> slots)
    {
        var objects = new PropertyObject[slots.Count];
        var keys = new List<string>();
        var seen = new HashSet<string>();
        for (int i = 0; i < slots.Count; i++)
        {
            // 缺席（或非对象）成员当空对象：键取 present 成员的并集，缺键逐项落 Multiple。
            objects[i] = slots[i].Present && slots[i].Value.ToObject(out var obj) ? obj : PropertyObject.Empty;
            foreach (var kvp in objects[i].Map)
                if (seen.Add(kvp.Key))
                    keys.Add(kvp.Key);   // 键并集（保序）
        }

        var map = new Map<string, PropertyValue>();
        var childSlots = new Slot[slots.Count];
        foreach (var key in keys)
        {
            for (int i = 0; i < objects.Length; i++)
                childSlots[i] = objects[i].Map.TryGetValue(key, out var value) ? Slot.Of(value) : Slot.Absent;
            map.Add(key, MergeValues(childSlots));
        }
        return new PropertyObject(map);
    }
}
