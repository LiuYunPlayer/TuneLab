using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

namespace TuneLab.Base.Event;

public interface IEvent<in TEvent>
{
    void Subscribe(TEvent invokable);
    void Unsubscribe(TEvent invokable);
}

public interface IActionEvent : IEvent<Action> { }
public interface IActionEvent<out T> : IEvent<Action<T>> { }
public interface IActionEvent<out T1, out T2> : IEvent<Action<T1, T2>> { }
public interface IActionEvent<out T1, out T2, out T3> : IEvent<Action<T1, T2, T3>> { }

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
}