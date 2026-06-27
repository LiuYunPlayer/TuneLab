using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public class ActionEvent : IActionEvent
{
    public static implicit operator Action(ActionEvent e) => e.Invoke;
    public void Subscribe(Action action) => Action += action;
    public void Unsubscribe(Action action) => Action -= action;
    public void Invoke() => Action?.Invoke();
    event Action? Action;
    public readonly static IActionEvent Empty = new EmptyEvent();
    sealed class EmptyEvent : IActionEvent
    {
        public void Subscribe(Action invokable) { }
        public void Unsubscribe(Action invokable) { }
    }
}

public class ActionEvent<T> : IActionEvent<T>
{
    public static implicit operator Action<T>(ActionEvent<T> e) => e.Invoke;
    public void Subscribe(Action<T> action) => Action += action;
    public void Unsubscribe(Action<T> action) => Action -= action;
    public void Invoke(T t) => Action?.Invoke(t);
    event Action<T>? Action;
    public readonly static IActionEvent<T> Empty = new EmptyEvent();
    sealed class EmptyEvent : IActionEvent<T>
    {
        public void Subscribe(Action<T> invokable) { }
        public void Unsubscribe(Action<T> invokable) { }
    }
}

public class ActionEvent<T1, T2> : IActionEvent<T1, T2>
{
    public static implicit operator Action<T1, T2>(ActionEvent<T1, T2> e) => e.Invoke;
    public void Subscribe(Action<T1, T2> action) => Action += action;
    public void Unsubscribe(Action<T1, T2> action) => Action -= action;
    public void Invoke(T1 t1, T2 t2) => Action?.Invoke(t1, t2);
    event Action<T1, T2>? Action;
    public readonly static IActionEvent<T1, T2> Empty = new EmptyEvent();
    sealed class EmptyEvent : IActionEvent<T1, T2>
    {
        public void Subscribe(Action<T1, T2> invokable) { }
        public void Unsubscribe(Action<T1, T2> invokable) { }
    }
}

public class ActionEvent<T1, T2, T3> : IActionEvent<T1, T2, T3>
{
    public static implicit operator Action<T1, T2, T3>(ActionEvent<T1, T2, T3> e) => e.Invoke;
    public void Subscribe(Action<T1, T2, T3> action) => Action += action;
    public void Unsubscribe(Action<T1, T2, T3> action) => Action -= action;
    public void Invoke(T1 t1, T2 t2, T3 t3) => Action?.Invoke(t1, t2, t3);
    event Action<T1, T2, T3>? Action;
    public readonly static IActionEvent<T1, T2, T3> Empty = new EmptyEvent();
    sealed class EmptyEvent : IActionEvent<T1, T2, T3>
    {
        public void Subscribe(Action<T1, T2, T3> invokable) { }
        public void Unsubscribe(Action<T1, T2, T3> invokable) { }
    }
}

public class ActionEvent<T1, T2, T3, T4> : IActionEvent<T1, T2, T3, T4>
{
    public static implicit operator Action<T1, T2, T3, T4>(ActionEvent<T1, T2, T3, T4> e) => e.Invoke;
    public void Subscribe(Action<T1, T2, T3, T4> action) => Action += action;
    public void Unsubscribe(Action<T1, T2, T3, T4> action) => Action -= action;
    public void Invoke(T1 t1, T2 t2, T3 t3, T4 t4) => Action?.Invoke(t1, t2, t3, t4);
    event Action<T1, T2, T3, T4>? Action;
    public readonly static IActionEvent<T1, T2, T3, T4> Empty = new EmptyEvent();
    sealed class EmptyEvent : IActionEvent<T1, T2, T3, T4>
    {
        public void Subscribe(Action<T1, T2, T3, T4> invokable) { }
        public void Unsubscribe(Action<T1, T2, T3, T4> invokable) { }
    }
}

public class ActionEvent<T1, T2, T3, T4, T5> : IActionEvent<T1, T2, T3, T4, T5>
{
    public static implicit operator Action<T1, T2, T3, T4, T5>(ActionEvent<T1, T2, T3, T4, T5> e) => e.Invoke;
    public void Subscribe(Action<T1, T2, T3, T4, T5> action) => Action += action;
    public void Unsubscribe(Action<T1, T2, T3, T4, T5> action) => Action -= action;
    public void Invoke(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5) => Action?.Invoke(t1, t2, t3, t4, t5);
    event Action<T1, T2, T3, T4, T5>? Action;
    public readonly static IActionEvent<T1, T2, T3, T4, T5> Empty = new EmptyEvent();
    sealed class EmptyEvent : IActionEvent<T1, T2, T3, T4, T5>
    {
        public void Subscribe(Action<T1, T2, T3, T4, T5> invokable) { }
        public void Unsubscribe(Action<T1, T2, T3, T4, T5> invokable) { }
    }
}

public class ActionEvent<T1, T2, T3, T4, T5, T6> : IActionEvent<T1, T2, T3, T4, T5, T6>
{
    public static implicit operator Action<T1, T2, T3, T4, T5, T6>(ActionEvent<T1, T2, T3, T4, T5, T6> e) => e.Invoke;
    public void Subscribe(Action<T1, T2, T3, T4, T5, T6> action) => Action += action;
    public void Unsubscribe(Action<T1, T2, T3, T4, T5, T6> action) => Action -= action;
    public void Invoke(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6) => Action?.Invoke(t1, t2, t3, t4, t5, t6);
    event Action<T1, T2, T3, T4, T5, T6>? Action;
    public readonly static IActionEvent<T1, T2, T3, T4, T5, T6> Empty = new EmptyEvent();
    sealed class EmptyEvent : IActionEvent<T1, T2, T3, T4, T5, T6>
    {
        public void Subscribe(Action<T1, T2, T3, T4, T5, T6> invokable) { }
        public void Unsubscribe(Action<T1, T2, T3, T4, T5, T6> invokable) { }
    }
}

public class ActionEvent<T1, T2, T3, T4, T5, T6, T7> : IActionEvent<T1, T2, T3, T4, T5, T6, T7>
{
    public static implicit operator Action<T1, T2, T3, T4, T5, T6, T7>(ActionEvent<T1, T2, T3, T4, T5, T6, T7> e) => e.Invoke;
    public void Subscribe(Action<T1, T2, T3, T4, T5, T6, T7> action) => Action += action;
    public void Unsubscribe(Action<T1, T2, T3, T4, T5, T6, T7> action) => Action -= action;
    public void Invoke(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7) => Action?.Invoke(t1, t2, t3, t4, t5, t6, t7);
    event Action<T1, T2, T3, T4, T5, T6, T7>? Action;
    public readonly static IActionEvent<T1, T2, T3, T4, T5, T6, T7> Empty = new EmptyEvent();
    sealed class EmptyEvent : IActionEvent<T1, T2, T3, T4, T5, T6, T7>
    {
        public void Subscribe(Action<T1, T2, T3, T4, T5, T6, T7> invokable) { }
        public void Unsubscribe(Action<T1, T2, T3, T4, T5, T6, T7> invokable) { }
    }
}

public class ActionEvent<T1, T2, T3, T4, T5, T6, T7, T8> : IActionEvent<T1, T2, T3, T4, T5, T6, T7, T8>
{
    public static implicit operator Action<T1, T2, T3, T4, T5, T6, T7, T8>(ActionEvent<T1, T2, T3, T4, T5, T6, T7, T8> e) => e.Invoke;
    public void Subscribe(Action<T1, T2, T3, T4, T5, T6, T7, T8> action) => Action += action;
    public void Unsubscribe(Action<T1, T2, T3, T4, T5, T6, T7, T8> action) => Action -= action;
    public void Invoke(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8) => Action?.Invoke(t1, t2, t3, t4, t5, t6, t7, t8);
    event Action<T1, T2, T3, T4, T5, T6, T7, T8>? Action;
    public readonly static IActionEvent<T1, T2, T3, T4, T5, T6, T7, T8> Empty = new EmptyEvent();
    sealed class EmptyEvent : IActionEvent<T1, T2, T3, T4, T5, T6, T7, T8>
    {
        public void Subscribe(Action<T1, T2, T3, T4, T5, T6, T7, T8> invokable) { }
        public void Unsubscribe(Action<T1, T2, T3, T4, T5, T6, T7, T8> invokable) { }
    }
}
