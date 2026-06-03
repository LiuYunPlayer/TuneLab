using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Primitives.Property;

namespace TuneLab.Foundation.Property;

// 多选编辑的数据源：把多个属性对象合成一个 IDataPropertyObject 外观。
// 读：各对象同 key 值全等则返该值，否则返 Invalid（控件据此显示"多值"）。
// 写：扇出到所有对象。撤销根 DataRoot 的 Head/Commit/DiscardTo 委托给首个对象——同一文档共享一个 Head，
// 一次提交即把所有对象的改动归为一个撤销单元；但 Modified 合并所有对象的修改事件，使扇出/撤销过程中
// "最后一次刷新"看到的是全部已写完的最终值（否则只听首对象会在它先被写、其余未写时算出 Invalid 并卡住）。
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
                return PropertyValue.Invalid;
        }
        return first;
    }

    public void SetValue(PropertyPath.Key key, PropertyValue value)
    {
        foreach (var dataObject in mDataObjects)
            dataObject.SetValue(key, value);
    }

    // 任一对象的 Modified 都转发给同一订阅者：扇出/撤销逐对象触发刷新，最后一次刷新时全部已写完 → 显示最终值。
    class MergedModifiedEvent(IReadOnlyList<DataPropertyObject> dataObjects) : IMergableEvent
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

        public void BeginMerge() { }
        public void EndMerge() { }
    }

    // 撤销根：撤销机制（Head/Commit/DiscardTo/Undo/Redo）委托首对象（文档级共享），Modified 取合并事件。
    // 本对象只是面板侧瞬态外观、从不挂进文档树，故文档机制内部成员（Parent/Children/Push 等）不会被调用。
    class MultiDataRoot : IDataObject
    {
        public MultiDataRoot(IReadOnlyList<DataPropertyObject> dataObjects)
        {
            mRoot = dataObjects[0];
            mModified = new MergedModifiedEvent(dataObjects);
        }

        public IMergableEvent Modified => mModified;
        public Head Head => mRoot.Head;
        public void Attach(IDataObject parent) { }
        public void Detach() { }
        public void BeginMergeNotify() => mRoot.BeginMergeNotify();
        public void EndMergeNotify() => mRoot.EndMergeNotify();
        public bool Commit() => mRoot.Commit();
        public bool Discard() => mRoot.Discard();
        public bool DiscardTo(Head head) => mRoot.DiscardTo(head);
        public bool Undo() => mRoot.Undo();
        public bool Redo() => mRoot.Redo();

        IDataObject? IDataObject.Parent { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        IList<IDataObject> IDataObject.Children => throw new NotSupportedException();
        int IDataObject.NotifyFlag { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        bool IDataObject.NeedNotifyInMerge { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        IDataObject.MergableModifiedEvent IDataObject.ModifiedEvent => throw new NotSupportedException();
        void IDataObject.Push(ICommand command) => throw new NotSupportedException();

        readonly DataPropertyObject mRoot;
        readonly MergedModifiedEvent mModified;
    }

    readonly IReadOnlyList<DataPropertyObject> mDataObjects;
    readonly MultiDataRoot mDataRoot;
}
