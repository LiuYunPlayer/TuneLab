using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 单选侧懒导航的内部契约：解析真实子节点（不创建）/ find-or-create 子节点。
// 「key」在对象节点是字段键、在数组节点是元素 token——两种节点（DataPropertyObject / DataPropertyArray）都实现它，
// 故 ObjectView / ArrayView 复用同一套懒链。不进公开接口（仅这两类视图与两个节点互链）。
internal interface ILazyObjectNode
{
    DataPropertyObject? FindObject(string key);
    DataPropertyObject GetOrCreateObject(string key);
    DataPropertyArray? FindArray(string key);
    DataPropertyArray GetOrCreateArray(string key);
}

// 嵌套对象节点的懒视图：表示 owner 下 key 处的对象。读经 owner.FindObject 返回 default（不创建），
// 写经 owner.GetOrCreateObject 按需建路径；借壳 root 转发整套文档身份（撤销/Modified 根锚最外层节点）。
// 解析出的子节点本身是 ILazyObjectNode（concrete DataPropertyObject/DataPropertyArray），故继续向下导航直接 cast 链。
internal sealed class ObjectView(IDataObject root, ILazyObjectNode owner, string key)
    : IDataObject.Wrapper(root), IDataPropertyObject, ILazyObjectNode
{
    public IDataPropertyObject Object(string subKey) => new ObjectView(root, this, subKey);
    public IDataPropertyArray Array(string subKey) => new ArrayView(root, this, subKey);

    public PropertyValue GetValue(string subKey, PropertyValue defaultValue)
        => owner.FindObject(key)?.GetValue(subKey, defaultValue) ?? defaultValue;

    public void SetValue(string subKey, PropertyValue value)
        => owner.GetOrCreateObject(key).SetValue(subKey, value);

    // 移除子键：对象缺席则无键可删（不创建）。
    public void RemoveValue(string subKey)
        => owner.FindObject(key)?.RemoveValue(subKey);

    DataPropertyObject? ILazyObjectNode.FindObject(string subKey) => ((ILazyObjectNode?)owner.FindObject(key))?.FindObject(subKey);
    DataPropertyObject ILazyObjectNode.GetOrCreateObject(string subKey) => ((ILazyObjectNode)owner.GetOrCreateObject(key)).GetOrCreateObject(subKey);
    DataPropertyArray? ILazyObjectNode.FindArray(string subKey) => ((ILazyObjectNode?)owner.FindObject(key))?.FindArray(subKey);
    DataPropertyArray ILazyObjectNode.GetOrCreateArray(string subKey) => ((ILazyObjectNode)owner.GetOrCreateObject(key)).GetOrCreateArray(subKey);
}

// 嵌套数组节点的懒视图：表示 owner 下 key 处的数组。结构读（Count/Tokens）经 FindArray 返回空（不创建），
// 结构写（Insert/Add）经 GetOrCreateArray 按需建路径；元素经 token 导航委托已解析数组（缺席则空导航/默认值/no-op）。
internal sealed class ArrayView(IDataObject root, ILazyObjectNode owner, string key)
    : IDataObject.Wrapper(root), IDataPropertyArray, ILazyObjectNode
{
    public int Count => owner.FindArray(key)?.Count ?? 0;
    public IReadOnlyList<string> Tokens => owner.FindArray(key)?.Tokens ?? [];
    // 借壳 root（= Wrapper 的 Modified）：数组缺席、或经 undo 重建为新实例时订阅仍有效（保守多触发，面板按 token diff 吸收）。
    public IModifiedEvent MembershipModified => Modified;

    public void Insert(int index, PropertyValue value) => owner.GetOrCreateArray(key).Insert(index, value);
    public void Add(PropertyValue value) => owner.GetOrCreateArray(key).Add(value);
    public void RemoveAt(int index) => owner.FindArray(key)?.RemoveAt(index);   // 缺席无元素可删

    public IDataPropertyObject Object(string token) => new ObjectView(root, this, token);
    public IDataPropertyArray Array(string token) => new ArrayView(root, this, token);
    public PropertyValue GetValue(string token, PropertyValue defaultValue) => owner.FindArray(key)?.GetValue(token, defaultValue) ?? defaultValue;
    public void SetValue(string token, PropertyValue value) => owner.FindArray(key)?.SetValue(token, value);
    public void RemoveValue(string token) => owner.FindArray(key)?.RemoveValue(token);

    DataPropertyObject? ILazyObjectNode.FindObject(string token) => ((ILazyObjectNode?)owner.FindArray(key))?.FindObject(token);
    DataPropertyObject ILazyObjectNode.GetOrCreateObject(string token) => ((ILazyObjectNode)owner.GetOrCreateArray(key)).GetOrCreateObject(token);
    DataPropertyArray? ILazyObjectNode.FindArray(string token) => ((ILazyObjectNode?)owner.FindArray(key))?.FindArray(token);
    DataPropertyArray ILazyObjectNode.GetOrCreateArray(string token) => ((ILazyObjectNode)owner.GetOrCreateArray(key)).GetOrCreateArray(token);
}
