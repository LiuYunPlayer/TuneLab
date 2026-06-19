using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 有序可重复列表的 live 形态，与 DataPropertyObject 并列。内部组合 DataObjectList<DataPropertyValue>：
// 每个元素槽是 DataPropertyValue（标量/子对象/子数组三选一），逐元素 attach/undo——中插/删/原位改都是
// 细粒度命令，元素（含嵌套对象/数组）有独立 live 身份。插件只见 PropertyArray 快照，永不拿 live 树。
public class DataPropertyArray : DataObject, IDataObject<PropertyArray>
{
    public int Count => mList.Count;

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

    readonly DataObjectList<DataPropertyValue> mList = new();
}
