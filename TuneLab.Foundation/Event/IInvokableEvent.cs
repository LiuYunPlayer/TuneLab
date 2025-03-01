namespace TuneLab.Foundation.Event;

public interface IInvokableEvent<out TEvent>
{
    TEvent InvokeEvent { get; }
}
