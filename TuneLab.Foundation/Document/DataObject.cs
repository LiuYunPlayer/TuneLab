using System;
using System.Collections.Generic;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

// 数据对象基类：维护父子树、传递 Push/Commit/Undo 等命令、处理 merge 通知。
// 与 info 无关——只负责文档机制；具体 info 类型由实现 IDataObject<T> 的子类（叶子 DataProperty<T> / 复合节点）承担。
// 撤销根是 DataDocument（override Push 记录未提交栈）；Push 沿 Parent 上爬到它，无 document 祖先即应用但不记录（构造/装载期）。
public abstract class DataObject : IDataObject
{
    public IModifiedEvent Modified => mModifiedEvent;
    // 改前事件：在值落地前触发，handler 内读值得旧值。与 Modified 不同，不参与 merge 合并——
    // "改前"语义不可延迟（merge 只折叠结果态通知，旧值必须在每次变更前都可读到）。
    public IActionEvent WillModified => mWillModifiedEvent;
    public virtual Head Head => mParent!.Head;

    public DataObject(IDataObject? parent = null)
    {
        if (parent != null)
            Attach(parent);
    }

    public void Attach(IDataObject parent) => ChangeParent((DataObject)parent);
    public void Detach() => ChangeParent(null);

    public IDisposable MergeNotify()
    {
        BeginMergeNotify();
        return new MergeScope(this);
    }

    public virtual bool Commit() => mParent?.Commit() ?? false;
    public virtual bool Discard() => mParent?.Discard() ?? false;
    public virtual bool DiscardTo(Head head) => mParent?.DiscardTo(head) ?? false;
    public virtual bool Undo() => mParent?.Undo() ?? false;
    public virtual bool Redo() => mParent?.Redo() ?? false;

    protected DataObject? Parent => mParent;

    public void BeginMergeNotify() => PushAndDo(new BeginMergeNotifyCommand(this));
    public void EndMergeNotify() => PushAndDo(new EndMergeNotifyCommand(this));

    protected virtual void Push(ICommand command) => mParent?.Push(command);

    protected void PushAndDo(ICommand command)
    {
        command.Redo();
        Push(command);
    }

    protected void Notify(bool notifyParent = true)
    {
        InvokeModified();
        if (notifyParent)
            mParent?.Notify();
    }

    protected void NotifyWill(bool notifyParent = true)
    {
        mWillModifiedEvent.Invoke();
        if (notifyParent)
            mParent?.NotifyWill();
    }

    void ChangeNotifyFlag(int delta)
    {
        mNotifyFlag += delta;
        foreach (var child in mChildren)
        {
            child.ChangeNotifyFlag(delta);
        }

        if (mNotifyFlag != 0)
            return;

        if (mNeedNotifyInMerge)
        {
            mNeedNotifyInMerge = false;
            mModifiedEvent.Invoke(false);
        }
    }

    void ChangeParent(DataObject? parent)
    {
        if (mParent == parent)
            return;

        var flagBefore = mParent == null ? 0 : mParent.mNotifyFlag;
        var flagAfter = parent == null ? 0 : parent.mNotifyFlag;
        int delta = flagAfter - flagBefore;

        mParent?.mChildren.Remove(this);
        mParent = parent;
        mParent?.mChildren.Add(this);

        if (delta == 0)
            return;

        ChangeNotifyFlag(delta);
    }

    void InvokeModified()
    {
        if (mNotifyFlag > 0)
        {
            mNeedNotifyInMerge = true;
            mModifiedEvent.Invoke(true);    // merge 期间的调整中间态：只通知全量订阅者
            return;
        }

        mModifiedEvent.Invoke(false);       // 结果态
    }

    class BeginMergeNotifyCommand(DataObject dataObject) : ICommand
    {
        public void Redo() => dataObject.ChangeNotifyFlag(1);
        public void Undo() => dataObject.ChangeNotifyFlag(-1);
    }

    class EndMergeNotifyCommand(DataObject dataObject) : ICommand
    {
        public void Redo() => dataObject.ChangeNotifyFlag(-1);
        public void Undo() => dataObject.ChangeNotifyFlag(1);
    }

    class MergeScope(DataObject dataObject) : IDisposable
    {
        public void Dispose() => dataObject.EndMergeNotify();
    }

    DataObject? mParent = null;
    readonly List<DataObject> mChildren = new();
    int mNotifyFlag = 0;
    bool mNeedNotifyInMerge = false;
    readonly ModifiedEvent mModifiedEvent = new();
    readonly ActionEvent mWillModifiedEvent = new();
}
