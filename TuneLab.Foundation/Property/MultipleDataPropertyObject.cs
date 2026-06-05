using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Primitives.Property;

namespace TuneLab.Foundation.Property;

// 多选编辑的数据源：把多个属性对象合成一个 IDataPropertyObject 外观。
// 读：三态——0 对象（无选中）返 Invalid；各对象同 key 不完全相等返 Multiple；全等返该值。
// 控件据此分别呈 Invalid（无值）/ Multiple（多值）/ 具体值。
// 写：扇出到所有对象。撤销根 DataRoot 的 Head/Commit/DiscardTo 委托给首个对象——同一文档共享一个 Head，
// 一次提交即把所有对象的改动归为一个撤销单元；但 Modified 合并所有对象的修改事件，使扇出/撤销过程中
// "最后一次刷新"看到的是全部已写完的最终值（否则只听首对象会在它先被写、其余未写时算出 Multiple 并卡住）。
// 允许 0 对象（无选中）：撤销机制成 no-op，仅供面板在遮罩下把控件绑出 Invalid 态。
public class MultipleDataPropertyObject : IDataPropertyObject
{
    public MultipleDataPropertyObject(IReadOnlyCollection<DataPropertyObject> dataObjects)
    {
        mDataObjects = dataObjects as IReadOnlyList<DataPropertyObject> ?? dataObjects.ToList();
        mDataRoot = new MultiDataRoot(mDataObjects);
    }

    public IDataObject DataRoot => mDataRoot;

    public PropertyValue GetValue(PropertyPath.Key key, PropertyValue defaultValue)
    {
        if (mDataObjects.Count == 0)
            return PropertyValue.Invalid;

        var first = mDataObjects[0].GetValue(key, defaultValue);
        for (int i = 1; i < mDataObjects.Count; i++)
        {
            if (!mDataObjects[i].GetValue(key, defaultValue).Equals(first))
                return PropertyValue.Multiple;
        }
        return first;
    }

    // 扇出期间合并通知：先让所有对象进 merge，再统一写值，最后统一退 merge。
    // 这样写到一半时各对象只发 canIgnore 中间通知（结果态订阅者不触发），全部写完退 merge 才发结果态，
    // 此刻每次刷新看到的都是"所有对象已写完"的最终值——消除"部分写完→瞬时 Multiple"的中间态闪烁
    // （否则会反复清空正在编辑的文本框/令 checkbox 闪回 dash）。三段分开循环是关键：必须所有 set 完成后才退首个 merge。
    public void SetValue(PropertyPath.Key key, PropertyValue value)
    {
        foreach (var dataObject in mDataObjects)
            dataObject.BeginMergeNotify();
        foreach (var dataObject in mDataObjects)
            dataObject.SetValue(key, value);
        foreach (var dataObject in mDataObjects)
            dataObject.EndMergeNotify();
    }

    // 任一对象的 Modified 都转发给同一订阅者：扇出/撤销逐对象触发刷新，最后一次刷新时全部已写完 → 显示最终值。
    // 两种订阅形状（无参=结果态、带 bool=全量）都转发到各对象的 Modified。
    class MergedModifiedEvent(IReadOnlyList<DataPropertyObject> dataObjects) : IModifiedEvent
    {
        public void Subscribe(Action invokable)
        {
            foreach (var dataObject in dataObjects)
                dataObject.Modified.Subscribe(invokable);
        }

        public void Unsubscribe(Action invokable)
        {
            foreach (var dataObject in dataObjects)
                dataObject.Modified.Unsubscribe(invokable);
        }

        public void Subscribe(Action<bool> invokable)
        {
            foreach (var dataObject in dataObjects)
                dataObject.Modified.Subscribe(invokable);
        }

        public void Unsubscribe(Action<bool> invokable)
        {
            foreach (var dataObject in dataObjects)
                dataObject.Modified.Unsubscribe(invokable);
        }
    }

    // 撤销根：撤销机制（Head/Commit/DiscardTo/Undo/Redo）委托首对象（文档级共享），Modified 取合并事件。
    // 本对象只是面板侧瞬态外观、从不挂进文档树，故文档机制内部成员（Parent/Children/Push 等）不会被调用。
    class MultiDataRoot : IDataObject
    {
        public MultiDataRoot(IReadOnlyList<DataPropertyObject> dataObjects)
        {
            mRoot = dataObjects.Count > 0 ? dataObjects[0] : null;
            mModified = new MergedModifiedEvent(dataObjects);
        }

        public IModifiedEvent Modified => mModified;
        public Head Head => mRoot?.Head ?? default;
        public void Attach(IDataObject parent) { }
        public void Detach() { }
        public IDisposable MergeNotify() => mRoot?.MergeNotify() ?? EmptyDisposable.Shared;
        public void BeginMergeNotify() => mRoot?.BeginMergeNotify();
        public void EndMergeNotify() => mRoot?.EndMergeNotify();
        public bool Commit() => mRoot?.Commit() ?? false;
        public bool Discard() => mRoot?.Discard() ?? false;
        public bool DiscardTo(Head head) => mRoot?.DiscardTo(head) ?? false;
        public bool Undo() => mRoot?.Undo() ?? false;
        public bool Redo() => mRoot?.Redo() ?? false;

        // 无选中（0 对象）时撤销机制委托对象为空，全部 no-op。
        readonly DataPropertyObject? mRoot;
        readonly MergedModifiedEvent mModified;
    }

    sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Shared = new();
        public void Dispose() { }
    }

    readonly IReadOnlyList<DataPropertyObject> mDataObjects;
    readonly MultiDataRoot mDataRoot;
}
