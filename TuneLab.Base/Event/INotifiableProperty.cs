namespace TuneLab.Base.Event;

public interface INotifiableProperty<T>
{
    IActionEvent Modified { get; }
    IActionEvent WillModify { get; }
    T Value { get; set; }
}
