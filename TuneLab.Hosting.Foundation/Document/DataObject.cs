using System;
using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 数据对象基类：维护父子树、传递 Push/Commit/Undo 等命令、处理 merge 通知。
// 与 info 无关——只负责文档机制；具体 info 类型由实现 IDataObject<T> 的子类（叶子 DataProperty<T> / 复合节点）承担。
// 撤销根是 DataDocument（override Push 记录未提交栈）；Push 沿 Parent 上爬到它，无 document 祖先即应用但不记录（构造/装载期）。
public abstract class DataObject : IDataObject
{
    public IModifiedEvent Modified => mModifiedEvent;
    // 改前事件：在值落地前触发，handler 内读值得旧值。merge 语义与 Modified 对偶（参照 ACE 的
    // aboutToModify）：作用域内首次 canIgnore=false 必达（订阅者在此抓旧值/作废旧区域），其余
    // canIgnore=true 可忽略——Modified 折叠掉的中间态，其"改前旧值"同样无需作废；收口时重置。
    public IModifiedEvent WillModify => mWillModifyEvent;
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

    protected void Notify(bool canIgnore = false, bool notifyParent = true)
    {
        // canIgnore（调整中间态）随通知沿父链上传、不被各级重判：只要本结点或上游任一层处于 merge 中，
        // 这条通知就是中间态，沿途所有结点都只发全量通道 Invoke(true)、不发 settled。settled 留到 merge 收口
        // 由 CloseMergeScope/NotifySettledUp 统一上行补发，使「结果态 = 用户提交」对每个观察者（含域外祖先）都成立。
        canIgnore |= mNotifyFlag > 0;
        if (canIgnore)
        {
            if (mNotifyFlag > 0)
                mNeedNotifyInMerge = true;      // 域内结点：欠一发 settled，收口由 ChangeNotifyFlag 下行补发
            mModifiedEvent.Invoke(true);        // 中间态：只通知全量订阅者
        }
        else
        {
            mModifiedEvent.Invoke(false);       // 结果态：两类订阅者都通知
        }

        if (notifyParent)
            mParent?.Notify(canIgnore);
    }

    protected void NotifyWill(bool notifyParent = true)
    {
        mWillModifyEvent.Invoke(mWillNotifyInMerge);
        if (mNotifyFlag > 0)
            mWillNotifyInMerge = true;

        // 显式沿父链上爬（而非依赖事件冒泡）：每级各自按自己的 merge 计数判定 canIgnore。
        if (notifyParent)
            mParent?.NotifyWill();
    }

    void ChangeNotifyFlag(int delta)
    {
        mNotifyFlag += delta;
        // 快照子表再迭代：下方收口补发 Modified 时订阅者可能就地改挂载（reparent），会改动 mChildren。
        foreach (var child in mChildren.ToArray())
        {
            child.ChangeNotifyFlag(delta);
        }

        if (mNotifyFlag != 0)
            return;

        mWillNotifyInMerge = false;
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

    // 关闭一层 merge 作用域（EndMergeNotify.Redo / BeginMergeNotify.Undo）：先 ChangeNotifyFlag(-1) 下行补发
    // 域内各结点欠的 settled（本地、不上行）；若本作用域真正闭合（flag 归 0，非仍被外层 merge 覆盖）且期间合并过变更，
    // 再沿父链向上补发一发 settled——域外祖先在 merge 期收到的是中间态(Invoke(true))，其 settled 欠到此刻统一发，
    // 故「结果态 = 用户提交」对祖先成立。
    void CloseMergeScope()
    {
        bool coalesced = mNeedNotifyInMerge;   // 本作用域根在 merge 期收到过变更（须在下行补发清零前抓取）
        ChangeNotifyFlag(-1);
        if (mNotifyFlag == 0 && coalesced)
            mParent?.NotifySettledUp();
    }

    // 向上补发 settled：遇到仍处于另一层 merge 中的祖先（flag>0）即停——它属于尚未闭合的外层作用域，
    // 其 settled 欠到那层收口（标记 pending，由那层的 CloseMergeScope 续传上行），此处不提前发。
    void NotifySettledUp()
    {
        if (mNotifyFlag > 0)
        {
            mNeedNotifyInMerge = true;
            return;
        }

        mModifiedEvent.Invoke(false);
        mParent?.NotifySettledUp();
    }

    class BeginMergeNotifyCommand(DataObject dataObject) : ICommand
    {
        public void Redo() => dataObject.ChangeNotifyFlag(1);
        public void Undo() => dataObject.CloseMergeScope();
    }

    class EndMergeNotifyCommand(DataObject dataObject) : ICommand
    {
        public void Redo() => dataObject.CloseMergeScope();
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
    bool mWillNotifyInMerge = false;
    readonly ModifiedEvent mModifiedEvent = new();
    readonly ModifiedEvent mWillModifyEvent = new();
}
