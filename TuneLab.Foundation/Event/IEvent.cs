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
}