using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Utils;

namespace TuneLab.Foundation.Event;

// 稳定通知句柄：持有一个可被替换的引用 T，使用者注册一次、当作不变量。
// 内部对象更换时由它负责把订阅从旧对象退订、改接到新对象（见 When），使用者无需感知。
// Modified/WillModify 通知的是“所持对象本身被替换”（与所持对象自身的内容变更事件正交）。
public interface IHolder<out T>
{
    IActionEvent WillModify { get; }
    IActionEvent Modified { get; }
    T? Value { get; }
}

public static class IHolderExtension
{
    // 跟随所持对象的某个事件：所持对象更换时自动退旧接新，使用者只订一次。
    public static IEvent<TEvent> When<T, TEvent>(this IHolder<T> holder, ISubscriber<T, TEvent> subscriber)
    {
        return new WhenEvent<T, TEvent>(holder, subscriber);
    }

    class WhenEvent<T, TEvent> : IEvent<TEvent>
    {
        public WhenEvent(IHolder<T> holder, ISubscriber<T, TEvent> subscriber)
        {
            mHolder = holder;
            mSubscriber = subscriber;

            mHolder.WillModify.Subscribe(OnWillModify);
            mHolder.Modified.Subscribe(OnModified);
        }

        public void Subscribe(TEvent invokable)
        {
            if (mHolder.Value != null)
                mSubscriber.Subscribe(mHolder.Value, invokable);

            mEvents.Add(invokable);
        }

        public void Unsubscribe(TEvent invokable)
        {
            if (mHolder.Value != null)
                mSubscriber.Unsubscribe(mHolder.Value, invokable);

            mEvents.Remove(invokable);
        }

        void OnWillModify()
        {
            if (mHolder.Value == null)
                return;

            foreach (var invokable in mEvents)
            {
                mSubscriber.Unsubscribe(mHolder.Value, invokable);
            }
        }

        void OnModified()
        {
            if (mHolder.Value == null)
                return;

            foreach (var invokable in mEvents)
            {
                mSubscriber.Subscribe(mHolder.Value, invokable);
            }
        }

        readonly IHolder<T> mHolder;
        readonly ISubscriber<T, TEvent> mSubscriber;
        readonly List<TEvent> mEvents = new();
    }

    public static IEvent<TEvent> When<T, TEvent>(this IHolder<T> holder, Func<T, IEvent<TEvent>> selector)
    {
        return holder.When(new SelectorSubscriber<T, TEvent>(selector));
    }

    public static IEvent<TEvent> When<T, TEvent>(this IHolder<T> holder, Action<T, TEvent> subscribe, Action<T, TEvent> unsubscribe)
    {
        return holder.When(new ActionSubscriber<T, TEvent>(subscribe, unsubscribe));
    }

    public static IHolder<U> Select<T, U>(this IHolder<T> holder, Func<T, U> selector) where U : class
    {
        return new SelectHolder<T, U>(holder, selector);
    }

    class SelectHolder<T, U>(IHolder<T> holder, Func<T, U> selector) : IHolder<U> where U : class
    {
        public IActionEvent WillModify => holder.WillModify;
        public IActionEvent Modified => holder.Modified;
        public U? Value => holder.Value == null ? null : selector(holder.Value);
    }
}
