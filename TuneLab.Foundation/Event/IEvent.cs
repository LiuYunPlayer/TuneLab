using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public interface IEvent<in TEvent>
{
    void Subscribe(TEvent invokable);
    void Unsubscribe(TEvent invokable);
}

public interface IActionEvent : IEvent<Action> { }
public interface IActionEvent<out T> : IEvent<Action<T>> { }
public interface IActionEvent<out T1, out T2> : IEvent<Action<T1, T2>> { }
public interface IActionEvent<out T1, out T2, out T3> : IEvent<Action<T1, T2, T3>> { }
public interface IActionEvent<out T1, out T2, out T3, out T4> : IEvent<Action<T1, T2, T3, T4>> { }
public interface IActionEvent<out T1, out T2, out T3, out T4, out T5> : IEvent<Action<T1, T2, T3, T4, T5>> { }
public interface IActionEvent<out T1, out T2, out T3, out T4, out T5, out T6> : IEvent<Action<T1, T2, T3, T4, T5, T6>> { }
public interface IActionEvent<out T1, out T2, out T3, out T4, out T5, out T6, out T7> : IEvent<Action<T1, T2, T3, T4, T5, T6, T7>> { }
public interface IActionEvent<out T1, out T2, out T3, out T4, out T5, out T6, out T7, out T8> : IEvent<Action<T1, T2, T3, T4, T5, T6, T7, T8>> { }

public static class IEventExtensions
{
    public static void Subscribe<TEvent>(this IEvent<TEvent> e, TEvent invokable, DisposableManager? context = null)
    {
        e.Subscribe(invokable);
        if (context == null)
            return;
        
        context.Add(new Subscription<TEvent>(e, invokable));
    }

    class Subscription<TEvent>(IEvent<TEvent> e, TEvent invokable) : IDisposable
    {
        public void Dispose()
        {
            e.Unsubscribe(invokable);
        }
    }

    // 把 IActionEvent<T...> 按相等坍缩成无参 IActionEvent：源以"逐位等于给定 value"的实参 Invoke 时，走这条无参事件。
    // 视图订阅者用原生委托身份进出；对源只挂一个转发器，首个订阅者到达时挂、末个离开时摘——无过滤包装、无映射表。
    // 注意：与 IHolder.When 同源约束——Subscribe/Unsubscribe 须用同一返回实例配对（常态走 Subscribe(h, context) 天然成立）。
    // C# 无可变元泛型，各 arity 只能逐个铺（与 Action<>/IActionEvent<> 自身一样手写到 8 元）；共性收进 CollapsedEvent 基类。
    public static IActionEvent When<T>(this IActionEvent<T> source, T value)
        => new CollapsedEvent<T>(source, value);

    public static IActionEvent When<T1, T2>(this IActionEvent<T1, T2> source, T1 value1, T2 value2)
        => new CollapsedEvent<T1, T2>(source, value1, value2);

    public static IActionEvent When<T1, T2, T3>(this IActionEvent<T1, T2, T3> source, T1 value1, T2 value2, T3 value3)
        => new CollapsedEvent<T1, T2, T3>(source, value1, value2, value3);

    public static IActionEvent When<T1, T2, T3, T4>(this IActionEvent<T1, T2, T3, T4> source, T1 value1, T2 value2, T3 value3, T4 value4)
        => new CollapsedEvent<T1, T2, T3, T4>(source, value1, value2, value3, value4);

    public static IActionEvent When<T1, T2, T3, T4, T5>(this IActionEvent<T1, T2, T3, T4, T5> source, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
        => new CollapsedEvent<T1, T2, T3, T4, T5>(source, value1, value2, value3, value4, value5);

    public static IActionEvent When<T1, T2, T3, T4, T5, T6>(this IActionEvent<T1, T2, T3, T4, T5, T6> source, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
        => new CollapsedEvent<T1, T2, T3, T4, T5, T6>(source, value1, value2, value3, value4, value5, value6);

    public static IActionEvent When<T1, T2, T3, T4, T5, T6, T7>(this IActionEvent<T1, T2, T3, T4, T5, T6, T7> source, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
        => new CollapsedEvent<T1, T2, T3, T4, T5, T6, T7>(source, value1, value2, value3, value4, value5, value6, value7);

    public static IActionEvent When<T1, T2, T3, T4, T5, T6, T7, T8>(this IActionEvent<T1, T2, T3, T4, T5, T6, T7, T8> source, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
        => new CollapsedEvent<T1, T2, T3, T4, T5, T6, T7, T8>(source, value1, value2, value3, value4, value5, value6, value7, value8);

    // 共性：自有多播 + 引用计数挂/摘源转发器；子类只给"如何挂/摘转发器"与"逐位相等判后 Fire"。
    abstract class CollapsedEvent : IActionEvent
    {
        public void Subscribe(Action handler)
        {
            if (mSettled == null)
                Attach();
            mSettled += handler;
        }

        public void Unsubscribe(Action handler)
        {
            mSettled -= handler;
            if (mSettled == null)
                Detach();
        }

        protected void Fire() => mSettled?.Invoke();
        protected abstract void Attach();
        protected abstract void Detach();

        event Action? mSettled;
    }

    sealed class CollapsedEvent<T>(IActionEvent<T> source, T value) : CollapsedEvent
    {
        protected override void Attach() => source.Subscribe(OnSource);
        protected override void Detach() => source.Unsubscribe(OnSource);
        void OnSource(T arg)
        {
            if (EqualityComparer<T>.Default.Equals(arg, value))
                Fire();
        }
    }

    sealed class CollapsedEvent<T1, T2>(IActionEvent<T1, T2> source, T1 value1, T2 value2) : CollapsedEvent
    {
        protected override void Attach() => source.Subscribe(OnSource);
        protected override void Detach() => source.Unsubscribe(OnSource);
        void OnSource(T1 arg1, T2 arg2)
        {
            if (EqualityComparer<T1>.Default.Equals(arg1, value1)
                && EqualityComparer<T2>.Default.Equals(arg2, value2))
                Fire();
        }
    }

    sealed class CollapsedEvent<T1, T2, T3>(IActionEvent<T1, T2, T3> source, T1 value1, T2 value2, T3 value3) : CollapsedEvent
    {
        protected override void Attach() => source.Subscribe(OnSource);
        protected override void Detach() => source.Unsubscribe(OnSource);
        void OnSource(T1 arg1, T2 arg2, T3 arg3)
        {
            if (EqualityComparer<T1>.Default.Equals(arg1, value1)
                && EqualityComparer<T2>.Default.Equals(arg2, value2)
                && EqualityComparer<T3>.Default.Equals(arg3, value3))
                Fire();
        }
    }

    sealed class CollapsedEvent<T1, T2, T3, T4>(IActionEvent<T1, T2, T3, T4> source, T1 value1, T2 value2, T3 value3, T4 value4) : CollapsedEvent
    {
        protected override void Attach() => source.Subscribe(OnSource);
        protected override void Detach() => source.Unsubscribe(OnSource);
        void OnSource(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            if (EqualityComparer<T1>.Default.Equals(arg1, value1)
                && EqualityComparer<T2>.Default.Equals(arg2, value2)
                && EqualityComparer<T3>.Default.Equals(arg3, value3)
                && EqualityComparer<T4>.Default.Equals(arg4, value4))
                Fire();
        }
    }

    sealed class CollapsedEvent<T1, T2, T3, T4, T5>(IActionEvent<T1, T2, T3, T4, T5> source, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5) : CollapsedEvent
    {
        protected override void Attach() => source.Subscribe(OnSource);
        protected override void Detach() => source.Unsubscribe(OnSource);
        void OnSource(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            if (EqualityComparer<T1>.Default.Equals(arg1, value1)
                && EqualityComparer<T2>.Default.Equals(arg2, value2)
                && EqualityComparer<T3>.Default.Equals(arg3, value3)
                && EqualityComparer<T4>.Default.Equals(arg4, value4)
                && EqualityComparer<T5>.Default.Equals(arg5, value5))
                Fire();
        }
    }

    sealed class CollapsedEvent<T1, T2, T3, T4, T5, T6>(IActionEvent<T1, T2, T3, T4, T5, T6> source, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6) : CollapsedEvent
    {
        protected override void Attach() => source.Subscribe(OnSource);
        protected override void Detach() => source.Unsubscribe(OnSource);
        void OnSource(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            if (EqualityComparer<T1>.Default.Equals(arg1, value1)
                && EqualityComparer<T2>.Default.Equals(arg2, value2)
                && EqualityComparer<T3>.Default.Equals(arg3, value3)
                && EqualityComparer<T4>.Default.Equals(arg4, value4)
                && EqualityComparer<T5>.Default.Equals(arg5, value5)
                && EqualityComparer<T6>.Default.Equals(arg6, value6))
                Fire();
        }
    }

    sealed class CollapsedEvent<T1, T2, T3, T4, T5, T6, T7>(IActionEvent<T1, T2, T3, T4, T5, T6, T7> source, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7) : CollapsedEvent
    {
        protected override void Attach() => source.Subscribe(OnSource);
        protected override void Detach() => source.Unsubscribe(OnSource);
        void OnSource(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        {
            if (EqualityComparer<T1>.Default.Equals(arg1, value1)
                && EqualityComparer<T2>.Default.Equals(arg2, value2)
                && EqualityComparer<T3>.Default.Equals(arg3, value3)
                && EqualityComparer<T4>.Default.Equals(arg4, value4)
                && EqualityComparer<T5>.Default.Equals(arg5, value5)
                && EqualityComparer<T6>.Default.Equals(arg6, value6)
                && EqualityComparer<T7>.Default.Equals(arg7, value7))
                Fire();
        }
    }

    sealed class CollapsedEvent<T1, T2, T3, T4, T5, T6, T7, T8>(IActionEvent<T1, T2, T3, T4, T5, T6, T7, T8> source, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8) : CollapsedEvent
    {
        protected override void Attach() => source.Subscribe(OnSource);
        protected override void Detach() => source.Unsubscribe(OnSource);
        void OnSource(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        {
            if (EqualityComparer<T1>.Default.Equals(arg1, value1)
                && EqualityComparer<T2>.Default.Equals(arg2, value2)
                && EqualityComparer<T3>.Default.Equals(arg3, value3)
                && EqualityComparer<T4>.Default.Equals(arg4, value4)
                && EqualityComparer<T5>.Default.Equals(arg5, value5)
                && EqualityComparer<T6>.Default.Equals(arg6, value6)
                && EqualityComparer<T7>.Default.Equals(arg7, value7)
                && EqualityComparer<T8>.Default.Equals(arg8, value8))
                Fire();
        }
    }
}