using System.Collections.Generic;
using System.Diagnostics;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 有序可重复列表的 live 形态，与 DataPropertyObject 并列。内部组合 DataObjectList<DataPropertyValue>：
// 每个元素槽是 DataPropertyValue（标量/子对象/子数组三选一），逐元素 attach/undo——中插/删/原位改都是
// 细粒度命令，元素（含嵌套对象/数组）有独立 live 身份。插件只见 PropertyArray 快照，永不拿 live 树。
// 同时实现 IDataPropertyArray（导航式外观）：元素以稳定 token 寻址，token 当 key 复用 Object/Array/GetValue/SetValue，
// 面板用现成的字段绑定机制原位 live-bind 每个元素；并实现 ILazyObjectNode（key=token）供 ObjectView/ArrayView 向元素内继续懒导航。
public class DataPropertyArray : DataObject, IDataObject<PropertyArray>, IDataPropertyArray, ILazyObjectNode
{
    public int Count => mList.Count;

    // 结构变化通知（增删/重排）。= 底层列表的结构事件；元素值原位编辑只动该元素 DataPropertyValue、不触发此事件。
    public IModifiedEvent StructureModified => mList.StructureModified;

    // 有序稳定 token：顺序随列表，身份按元素实例惰性赋号、跨 undo/redo 稳定。
    public IReadOnlyList<string> Tokens
    {
        get
        {
            var tokens = new List<string>(mList.Count);
            foreach (var dataPropertyValue in mList)
                tokens.Add(GetOrAssignToken(dataPropertyValue));
            return tokens;
        }
    }

    public DataPropertyArray(IDataObject? parent = null) : base(parent)
    {
        mList.Attach(this);
    }

    public PropertyValue GetValue(int index) => mList[index].Value.ToPropertyValue();

    // 中插一个元素：先建带值的叶子（经 Canonicalize 维护标量/子对象/子数组不变量），再插入列表。
    public void Insert(int index, PropertyValue value)
    {
        var dataPropertyValue = new DataPropertyValue();
        dataPropertyValue.SetInfo(PropertySlot.Canonicalize(value));
        mList.Insert(index, dataPropertyValue);
    }

    public void Add(PropertyValue value) => Insert(Count, value);

    public void RemoveAt(int index) => mList.RemoveAt(index);

    // 原位改某元素值（撤销由该元素 DataPropertyValue 自带命令承担）。
    public void SetValue(int index, PropertyValue value) => mList[index].Set(PropertySlot.Canonicalize(value));

    // ===== token-as-key 导航外观（IDataPropertyObject）：元素经稳定 token 寻址，复用对象字段绑定机制 =====

    public IDataPropertyObject Object(string token) => new ObjectView(this, this, token);

    public IDataPropertyArray Array(string token) => new ArrayView(this, this, token);

    // 本层叶子读：陈旧 token（元素已删）退回 default；存值类型与 default 不符（如配置改过类型）一律退回 default。
    public PropertyValue GetValue(string token, PropertyValue defaultValue)
    {
        var dataPropertyValue = ResolveToken(token);
        if (dataPropertyValue == null)
            return defaultValue;

        var propertyValue = dataPropertyValue.Value.ToPropertyValue();
        return propertyValue.TypeEquals(defaultValue) ? propertyValue : defaultValue;
    }

    // 本层叶子写：陈旧 token → no-op（绑定元素已被删，写入作废）。撤销由 DataPropertyValue 自带命令承担。
    public void SetValue(string token, PropertyValue value) => ResolveToken(token)?.Set(PropertySlot.Canonicalize(value));

    // 按 token 移除元素（IDataPropertyObject.RemoveValue 在数组上的语义 = 删该元素）：解析 token → RemoveAt。陈旧 token = no-op。
    public void RemoveValue(string token)
    {
        var dataPropertyValue = ResolveToken(token);
        if (dataPropertyValue == null)
            return;
        int index = mList.IndexOf(dataPropertyValue);
        if (index >= 0)
            mList.RemoveAt(index);
    }

    // ===== ILazyObjectNode（key=元素 token）：解析到该元素槽的子对象/子数组，供 ObjectView/ArrayView 向元素内懒导航 =====

    DataPropertyObject? ILazyObjectNode.FindObject(string token) => ResolveToken(token)?.Value.Object;

    DataPropertyObject ILazyObjectNode.GetOrCreateObject(string token)
    {
        var dataPropertyValue = ResolveToken(token);
        Debug.Assert(dataPropertyValue != null, "GetOrCreateObject 收到陈旧 token：绑定元素已被删除");
        if (dataPropertyValue == null)
            return new DataPropertyObject();   // 防御：写入落到游离对象、不污染列表

        if (dataPropertyValue.Value.Object is { } existing)
            return existing;

        var child = new DataPropertyObject();
        dataPropertyValue.Set(new PropertySlot(child));
        return child;
    }

    DataPropertyArray? ILazyObjectNode.FindArray(string token) => ResolveToken(token)?.Value.Array;

    DataPropertyArray ILazyObjectNode.GetOrCreateArray(string token)
    {
        var dataPropertyValue = ResolveToken(token);
        Debug.Assert(dataPropertyValue != null, "GetOrCreateArray 收到陈旧 token：绑定元素已被删除");
        if (dataPropertyValue == null)
            return new DataPropertyArray();   // 防御：写入落到游离数组、不污染列表

        if (dataPropertyValue.Value.Array is { } existing)
            return existing;

        var child = new DataPropertyArray();
        dataPropertyValue.Set(new PropertySlot(child));
        return child;
    }

    public PropertyArray GetInfo()
    {
        var values = new List<PropertyValue>(mList.Count);
        foreach (var dataPropertyValue in mList)
            values.Add(dataPropertyValue.Value.ToPropertyValue());   // 子节点槽经子树值快照递归触底
        return new PropertyArray(values);
    }

    public void SetInfo(PropertyArray info)
    {
        var items = new List<DataPropertyValue>(info.Count);
        foreach (var value in info)
        {
            DataPropertyValue dataPropertyValue = new();
            dataPropertyValue.SetInfo(PropertySlot.Canonicalize(value));
            items.Add(dataPropertyValue);
        }
        mList.SetInfo(items);
    }

    // token：按元素实例身份（ReferenceEqualityComparer）惰性赋号 "e"+counter；同一实例恒得同号，
    // 跨 undo/redo 因 DataObjectList 重挂的是同一元素实例而稳定。删除后实例虽离列表仍留映射（撤销可复活时身份不变）。
    string GetOrAssignToken(DataPropertyValue dataPropertyValue)
    {
        if (!mTokens.TryGetValue(dataPropertyValue, out var token))
        {
            token = "e" + mTokenCounter++;
            mTokens[dataPropertyValue] = token;
        }
        return token;
    }

    // token → 当前在列表中的元素槽（不在列表 = 陈旧 token，返回 null）。
    DataPropertyValue? ResolveToken(string token)
    {
        foreach (var dataPropertyValue in mList)
            if (GetOrAssignToken(dataPropertyValue) == token)
                return dataPropertyValue;
        return null;
    }

    readonly DataObjectList<DataPropertyValue> mList = new();
    readonly Dictionary<DataPropertyValue, string> mTokens = new(ReferenceEqualityComparer.Instance);
    int mTokenCounter;
}
