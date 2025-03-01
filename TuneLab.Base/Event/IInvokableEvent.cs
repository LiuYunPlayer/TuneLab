namespace TuneLab.Base.Event;

public interface IInvokableEvent<out TEvent>
{
    TEvent InvokeEvent { get; }
}
