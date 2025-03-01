namespace TuneLab.Base.Event;

public class NotifiableProperty<T>(T defaultValue) : INotifiableProperty<T> where T : notnull
{
    public IActionEvent WillModify => mWillModify;
    public IActionEvent Modified => mModified;

    public static implicit operator NotifiableProperty<T>(T value)
    {
        return new NotifiableProperty<T>(value);
    }

    public static implicit operator T(NotifiableProperty<T> property)
    {
        return property.Value;
    }

    public T Value
    {
        get => mValue;
        set
        {
            if (Equals(value, mValue))
                return;

            mWillModify.Invoke();
            mValue = value;
            mModified.Invoke();
        }
    }

    T mValue = defaultValue;

    readonly ActionEvent mWillModify = new();
    readonly ActionEvent mModified = new();
}
