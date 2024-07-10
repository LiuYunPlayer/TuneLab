using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

namespace TuneLab.Base.Event;

public class ActionEvent : IActionEvent
{
    public static implicit operator Action(ActionEvent e) => e.Invoke;

    public void Subscribe(Action action)
    {
        Action += action;
    }

    public void Unsubscribe(Action action)
    {
        Action -= action;
    }

    public void Invoke()
    {
        Action?.Invoke();
    }

    event Action? Action;

    public readonly static IActionEvent Empty = new EmptyEvent();
    class EmptyEvent : IActionEvent
    {
        public void Subscribe(Action invokable) { }
        public void Unsubscribe(Action invokable) { }
    }
}

public class ActionEvent<T> : IActionEvent<T>
{
    public static implicit operator Action<T>(ActionEvent<T> e) => e.Invoke;

    public void Subscribe(Action<T> action)
    {
        Action += action;
    }

    public void Unsubscribe(Action<T> action)
    {
        Action -= action;
    }

    public void Invoke(T t)
    {
        Action?.Invoke(t);
    }

    event Action<T>? Action;

    public readonly static IActionEvent<T> Empty = new EmptyEvent();
    class EmptyEvent : IActionEvent<T>
    {
        public void Subscribe(Action<T> invokable) { }
        public void Unsubscribe(Action<T> invokable) { }
    }
}

public class ActionEvent<T1, T2> : IActionEvent<T1, T2>
{
    public static implicit operator Action<T1, T2>(ActionEvent<T1, T2> e) => e.Invoke;

    public void Subscribe(Action<T1, T2> action)
    {
        Action += action;
    }

    public void Unsubscribe(Action<T1, T2> action)
    {
        Action -= action;
    }

    public void Invoke(T1 t1, T2 t2)
    {
        Action?.Invoke(t1, t2);
    }

    event Action<T1, T2>? Action;

    public readonly static IActionEvent<T1, T2> Empty = new EmptyEvent();
    class EmptyEvent : IActionEvent<T1, T2>
    {
        public void Subscribe(Action<T1, T2> invokable) { }
        public void Unsubscribe(Action<T1, T2> invokable) { }
    }
}

public class ActionEvent<T1, T2, T3> : IActionEvent<T1, T2, T3>
{
    public static implicit operator Action<T1, T2, T3>(ActionEvent<T1, T2, T3> e) => e.Invoke;

    public void Subscribe(Action<T1, T2, T3> action)
    {
        Action += action;
    }

    public void Unsubscribe(Action<T1, T2, T3> action)
    {
        Action -= action;
    }

    public void Invoke(T1 t1, T2 t2, T3 t3)
    {
        Action?.Invoke(t1, t2, t3);
    }

    event Action<T1, T2, T3>? Action;

    public readonly static IActionEvent<T1, T2, T3> Empty = new EmptyEvent();
    class EmptyEvent : IActionEvent<T1, T2, T3>
    {
        public void Subscribe(Action<T1, T2, T3> invokable) { }
        public void Unsubscribe(Action<T1, T2, T3> invokable) { }
    }
}
