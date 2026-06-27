using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 多选编辑下的复合数组外观：把多个 IDataPropertyArray 按 index 对齐成一个三态数组（与标量多选的
// MultipleDataPropertyObject 对称，只是项以 index 对齐而非 key）。长度取最长成员；index i 的值——各成员该位
// 全等给该值、值不等给 Multiple。**「缺位 = 该位默认值」**：某成员短于 index i 时，该位按元素默认值参与比较
// （与标量缺 key 取默认同理）——故一个 note 把数组物化、另一个仍 absent 时，值恰等于默认的那些位不会误报 Multiple。
// 写**编辑即补齐**：扇出时把短于 index 的成员先按各位默认值补齐到该长度、再写入（实现「多选把不等长数组调成一样长」），
// 默认值由控制器经 SetElementDefaults 注入（= 各元素 config 的默认值）。结构操作（Add/Insert/RemoveAt）扇出到适用成员，
// 各操作经 merge 合并通知消中间态闪烁。
// 元素 token = index 的十进制串：跨成员稳定的元素身份并不存在（各成员各自的 token 互不相干），index 即唯一可对齐的键；
// 故结构变化会令 token 漂移、面板按 index keyed-diff 重建受影响行（多选下结构编辑少见，可接受）。
// IDataObject / merge / Modified 等文档面委托内部 MultipleDataPropertyObject（成员本身即 IDataPropertyObject），
// 与标量多选共用同一套扇出 / 合并通知 / 撤销根逻辑。0 成员（无选中）= 空数组、全 no-op。
public sealed class MultipleDataPropertyArray : IDataPropertyArray
{
    public MultipleDataPropertyArray(IReadOnlyCollection<IDataPropertyArray> arrays)
    {
        mArrays = arrays as IReadOnlyList<IDataPropertyArray> ?? arrays.ToList();
        mBase = new MultipleDataPropertyObject(mArrays);
    }

    public int Count
    {
        get
        {
            int max = 0;
            foreach (var array in mArrays)
                if (array.Count > max)
                    max = array.Count;
            return max;
        }
    }

    public IReadOnlyList<string> Tokens
    {
        get
        {
            int count = Count;
            var tokens = new string[count];
            for (int i = 0; i < count; i++)
                tokens[i] = i.ToString();
            return tokens;
        }
    }

    public IModifiedEvent MembershipModified => mBase.Modified;

    // 控制器注入的各元素默认值（= element config 默认值）：缺位读取的回退值 + 编辑补齐时填短成员的值。
    public void SetElementDefaults(IReadOnlyList<PropertyValue> defaults) => mElementDefaults = defaults;

    // 读：各成员该 index 全等→该值，不等→Multiple；某成员短于该位 → 取该位默认值参与比较（缺位=默认）。
    public PropertyValue GetValue(string token, PropertyValue defaultValue)
    {
        if (!TryIndex(token, out var index))
            return defaultValue;

        var fallback = index < mElementDefaults.Count ? mElementDefaults[index] : defaultValue;
        PropertyValue first = default;
        bool hasFirst = false;
        foreach (var array in mArrays)
        {
            var value = index < array.Count ? array.GetValue(array.Tokens[index], fallback) : fallback;
            if (!hasFirst) { first = value; hasFirst = true; }
            else if (!value.Equals(first)) return PropertyValue.Multiple;
        }
        return hasFirst ? first : fallback;
    }

    // 写（编辑即补齐）：扇出到所有成员——短于该位的先按各位默认值补齐到该长度、再写入；已有该位的直接写。
    public void SetValue(string token, PropertyValue value)
    {
        if (!TryIndex(token, out var index))
            return;
        FanOut(array =>
        {
            for (int k = array.Count; k <= index; k++)
                array.Add(k < mElementDefaults.Count ? mElementDefaults[k] : PropertyValue.Null);
            array.SetValue(array.Tokens[index], value);
        });
    }

    public void RemoveValue(string token)
    {
        if (!TryIndex(token, out var index))
            return;
        FanOut(array => { if (index < array.Count) array.RemoveValue(array.Tokens[index]); });
    }

    // 导航进对象/数组元素：复合「拥有该位」的成员的对应子节点（更短成员不参与），递归出嵌套多选外观。
    public IDataPropertyObject Object(string token)
    {
        if (!TryIndex(token, out var index))
            return new MultipleDataPropertyObject([]);
        var members = new List<IDataPropertyObject>();
        foreach (var array in mArrays)
            if (index < array.Count)
                members.Add(array.Object(array.Tokens[index]));
        return new MultipleDataPropertyObject(members);
    }

    public IDataPropertyArray Array(string token)
    {
        if (!TryIndex(token, out var index))
            return new MultipleDataPropertyArray([]);
        var members = new List<IDataPropertyArray>();
        foreach (var array in mArrays)
            if (index < array.Count)
                members.Add(array.Array(array.Tokens[index]));
        return new MultipleDataPropertyArray(members);
    }

    // 结构操作扇出：Add 追加到所有成员；Insert 插到能容纳该位的成员（更短的追加到尾）；RemoveAt 删掉拥有该位的成员。
    public void Add(PropertyValue value) => FanOut(array => array.Add(value));

    public void Insert(int index, PropertyValue value)
        => FanOut(array => { if (index <= array.Count) array.Insert(index, value); else array.Add(value); });

    public void RemoveAt(int index)
        => FanOut(array => { if (index < array.Count) array.RemoveAt(index); });

    // 扇出三段式（同 MultipleDataPropertyObject.SetValue）：先全员进 merge、再统一操作、最后统一退 merge，
    // 消除「部分成员写完→瞬时 Multiple」的中间态闪烁。
    void FanOut(Action<IDataPropertyArray> action)
    {
        foreach (var array in mArrays)
            array.BeginMergeNotify();
        foreach (var array in mArrays)
            action(array);
        foreach (var array in mArrays)
            array.EndMergeNotify();
    }

    static bool TryIndex(string token, out int index) => int.TryParse(token, out index);

    // ---- IDataObject / merge / Modified：委托内部多选对象（成员即 IDataPropertyObject）----
    public IModifiedEvent Modified => mBase.Modified;
    public IModifiedEvent WillModify => mBase.WillModify;
    public Head Head => mBase.Head;
    public void Attach(IDataObject parent) => mBase.Attach(parent);
    public void Detach() => mBase.Detach();
    public IDisposable MergeNotify() => mBase.MergeNotify();
    public void BeginMergeNotify() => mBase.BeginMergeNotify();
    public void EndMergeNotify() => mBase.EndMergeNotify();
    public bool Commit() => mBase.Commit();
    public bool Discard() => mBase.Discard();
    public bool DiscardTo(Head head) => mBase.DiscardTo(head);
    public bool Undo() => mBase.Undo();
    public bool Redo() => mBase.Redo();

    readonly IReadOnlyList<IDataPropertyArray> mArrays;
    readonly MultipleDataPropertyObject mBase;
    IReadOnlyList<PropertyValue> mElementDefaults = [];
}
