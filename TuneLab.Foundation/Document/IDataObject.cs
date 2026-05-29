using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public interface IDataObject
{
    IMergableEvent Modified { get; }
    Head Head { get; }
    void Attach(IDataObject parent);
    void Detach();
    void BeginMergeNotify();
    void EndMergeNotify();
    bool Commit();
    bool Discard();
    bool DiscardTo(Head head);
    bool Undo();
    bool Redo();

    protected IDataObject? Parent { get; set; }
    protected IList<IDataObject> Children { get; }
    protected int NotifyFlag { get; set; }
    protected bool NeedNotifyInMerge { get; set; }
    protected MergableModifiedEvent ModifiedEvent { get; }

    class MergableModifiedEvent(IDataObject dataObject) : ActionEvent, IMergableEvent
    {
        public void BeginMerge()
        {
            dataObject.BeginMergeNotify();
        }

        public void EndMerge()
        {
            dataObject.EndMergeNotify();
        }
    }

    protected void Push(ICommand command);

    protected void Notify(bool notifyParent = true)
    {
        InvokeModified();
        if (notifyParent)
            Parent?.Notify();
    }

    private void ChangeNotifyFlag(int delta)
    {
        NotifyFlag += delta;
        foreach (var child in Children)
        {
            child.ChangeNotifyFlag(delta);
        }

        if (NotifyFlag != 0)
            return;

        if (NeedNotifyInMerge)
        {
            NeedNotifyInMerge = false;
            ModifiedEvent.Invoke();
        }
    }

    private void ChangeParent(IDataObject? parent)
    {
        if (Parent == parent)
            return;

        var flagBefore = Parent == null ? 0 : Parent.NotifyFlag;
        var flagAfter = parent == null ? 0 : parent.NotifyFlag;
        int delta = flagAfter - flagBefore;

        Parent?.Children.Remove(this);
        Parent = parent;
        Parent?.Children.Add(this);

        if (delta == 0)
            return;

        ChangeNotifyFlag(delta);
    }

    private void InvokeModified()
    {
        if (NotifyFlag > 0)
        {
            NeedNotifyInMerge = true;
            return;
        }

        ModifiedEvent.Invoke();
    }

    public class Implementation : IDataObject
    {
        public IMergableEvent Modified => ((IDataObject)this).ModifiedEvent;
        public virtual Head Head => ((IDataObject)this).Parent!.Head;

        public Implementation()
        {
            mModifiedEvent = new(this);
        }

        public void Attach(IDataObject parent)
        {
            ((IDataObject)this).ChangeParent(parent);
        }

        public void Detach()
        {
            ((IDataObject)this).ChangeParent(null);
        }

        public void BeginMergeNotify()
        {
            PushAndDo(new BeginMergeNotifyCommand(this));
        }

        public void EndMergeNotify()
        {
            PushAndDo(new EndMergeNotifyCommand(this));
        }

        public virtual bool Commit()
        {
            if (((IDataObject)this).Parent == null)
                return false;

            return ((IDataObject)this).Parent.Commit();
        }

        public virtual bool Discard()
        {
            if (((IDataObject)this).Parent == null)
                return false;

            return ((IDataObject)this).Parent.Discard();
        }

        public virtual bool DiscardTo(Head head)
        {
            if (((IDataObject)this).Parent == null)
                return false;

            return ((IDataObject)this).Parent.DiscardTo(head);
        }

        public virtual bool Undo()
        {
            if (((IDataObject)this).Parent == null)
                return false;

            return ((IDataObject)this).Parent.Undo();
        }

        public virtual bool Redo()
        {
            if (((IDataObject)this).Parent == null)
                return false;

            return ((IDataObject)this).Parent.Redo();
        }

        protected virtual void Push(ICommand command)
        {
            ((IDataObject)this).Parent?.Push(command);
        }

        protected void PushAndDo(ICommand command)
        {
            command.Redo();
            Push(command);
        }

        protected void Notify() => ((IDataObject)this).Notify();

        class BeginMergeNotifyCommand(Implementation implementation) : ICommand
        {
            public void Redo()
            {
                ((IDataObject)implementation).ChangeNotifyFlag(1);
            }

            public void Undo()
            {
                ((IDataObject)implementation).ChangeNotifyFlag(-1);
            }
        }

        class EndMergeNotifyCommand(Implementation implementation) : ICommand
        {
            public void Redo()
            {
                ((IDataObject)implementation).ChangeNotifyFlag(-1);
            }

            public void Undo()
            {
                ((IDataObject)implementation).ChangeNotifyFlag(1);
            }
        }

        IDataObject? IDataObject.Parent { get; set; } = null;
        IList<IDataObject> IDataObject.Children { get; } = new List<IDataObject>();
        int IDataObject.NotifyFlag { get; set; } = 0;
        bool IDataObject.NeedNotifyInMerge { get; set; } = false;

        MergableModifiedEvent IDataObject.ModifiedEvent => mModifiedEvent;
        readonly MergableModifiedEvent mModifiedEvent;

        void IDataObject.Push(ICommand command) => Push(command);
    }

    internal class Wrapper(IDataObject dataObject) : IDataObject
    {
        public IMergableEvent Modified => dataObject.Modified;
        public Head Head => dataObject.Head;
        public void Attach(IDataObject parent) => dataObject.Attach(parent);
        public void BeginMergeNotify() => dataObject.BeginMergeNotify();
        public bool Commit() => dataObject.Commit();
        public void Detach() => dataObject.Detach();
        public void EndMergeNotify() => dataObject.EndMergeNotify();
        public bool Redo() => dataObject.Redo();
        public bool Discard() => dataObject.Discard();
        public bool DiscardTo(Head head) => dataObject.DiscardTo(head);
        public bool Undo() => dataObject.Undo();
        protected void Notify() => dataObject.Notify();

        IDataObject? IDataObject.Parent { get => dataObject.Parent; set => dataObject.Parent = value; }
        IList<IDataObject> IDataObject.Children => dataObject.Children;
        int IDataObject.NotifyFlag { get => dataObject.NotifyFlag; set => dataObject.NotifyFlag = value; }
        bool IDataObject.NeedNotifyInMerge { get => dataObject.NeedNotifyInMerge; set => dataObject.NeedNotifyInMerge = value; }
        MergableModifiedEvent IDataObject.ModifiedEvent => dataObject.ModifiedEvent;
        void IDataObject.Push(ICommand command) => dataObject.Push(command);
    }
}

public interface IReadOnlyDataObject<out T> : IDataObject
{
    T GetInfo();
}

public interface IDataObject<T> : IReadOnlyDataObject<T>
{
    public void Set(T info)
    {
        Set(this, info);
    }

    protected static void Set(IDataObject<T> dataObject, T info)
    {
        var before = dataObject.GetInfo();
        if (Equals(before, info))
            return;

        dataObject.SetInfoAndNotify(info);
        if (dataObject.Parent == null)
            return;

        dataObject.Push(new ModifyCommand(dataObject, before, info));
    }

    protected abstract void SetInfo(T info);

    protected static void SetInfo<TInfo>(IDataObject<TInfo> dataObject, TInfo info)
    {
        dataObject.SetInfo(info);
        dataObject.Notify(false);
    }

    private void SetInfoAndNotify(T info)
    {
        SetInfo(info);
        Notify();
    }

    private class ModifyCommand(IDataObject<T> dataObject, T before, T after) : ICommand
    {
        public void Redo()
        {
            dataObject.SetInfoAndNotify(after);
        }

        public void Undo()
        {
            dataObject.SetInfoAndNotify(before);
        }
    }

    internal new class Wrapper(IDataObject<T> dataObject) : IDataObject.Wrapper(dataObject), IDataObject<T>
    {
        protected virtual T FromGet(T info) => info;
        protected virtual T ToSet(T info) => info;

        public T GetInfo() => FromGet(dataObject.GetInfo());
        void IDataObject<T>.SetInfo(T info) => ((IDataObject<T>)this).SetInfo(ToSet(info));
    }
}

public static class IDataObjectExtension
{
    public static void Set<T>(this IDataObject<T> dataObject, T info)
    {
        dataObject.Set(info);
    }
}