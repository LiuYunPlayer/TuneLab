using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Base.Utils;

namespace TuneLab.Base.Event;

public class Owner<T> : IProvider<T> where T : class
{
    public IActionEvent ObjectWillChange => mObjectWillChanged;
    public IActionEvent ObjectChanged => mObjectChanged;
    public T? Object
    {
        get => mObject;
        set => Set(value);
    }

    public static implicit operator T?(Owner<T> owner)
    {
        return owner.Object;
    }

    public void Set(T? newDataObject)
    {
        if (mObject == newDataObject)
            return;

        mObjectWillChanged.Invoke();
        mObject = newDataObject;
        mObjectChanged.Invoke();
    }

    IEvent<TEvent> IProvider<T>.When<TEvent>(ISubscriber<T, TEvent> subscriber)
    {
        return new WhenEvent<TEvent>(this, subscriber);
    }

    class WhenEvent<TEvent> : IEvent<TEvent>
    {
        public WhenEvent(Owner<T> owner, ISubscriber<T, TEvent> subscriber)
        {
            mOwner = owner;
            mSubscriber = subscriber;

            mOwner.ObjectWillChange.Subscribe(OnObjectWillChange);
            mOwner.ObjectChanged.Subscribe(OnObjectChanged);
        }

        public void Subscribe(TEvent invokable)
        {
            if (mOwner.Object != null)
                mSubscriber.Subscribe(mOwner.Object, invokable);

            mEvents.Add(invokable);
        }

        public void Unsubscribe(TEvent invokable)
        {
            if (mOwner.Object != null)
                mSubscriber.Unsubscribe(mOwner.Object, invokable);

            mEvents.Remove(invokable);
        }

        void OnObjectWillChange()
        {
            if (mOwner.Object == null)
                return;

            foreach (var invokable in mEvents)
            {
                mSubscriber.Unsubscribe(mOwner.Object, invokable);
            }
        }

        void OnObjectChanged()
        {
            if (mOwner.Object == null)
                return;

            foreach (var invokable in mEvents)
            {
                mSubscriber.Subscribe(mOwner.Object, invokable);
            }
        }

        readonly Owner<T> mOwner;
        readonly ISubscriber<T, TEvent> mSubscriber;
        readonly List<TEvent> mEvents = new();
    }

    T? mObject;
    readonly ActionEvent mObjectWillChanged = new();
    readonly ActionEvent mObjectChanged = new();
}
