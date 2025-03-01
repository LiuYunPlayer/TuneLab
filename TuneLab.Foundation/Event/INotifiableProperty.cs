namespace TuneLab.Foundation.Event;

public interface INotifiableProperty<T>
{
    IActionEvent Modified { get; }
    IActionEvent WillModify { get; }
    T Value { get; set; }
}
