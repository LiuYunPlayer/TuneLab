using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Primitives.Event;

namespace TuneLab.Foundation.Event;

public class NotifiableProperty<T>(T defaultValue) : INotifiableProperty<T>, IReadOnlyNotifiableProperty<T> where T : notnull
{
    public IActionEvent WillModify => mWillModify;
    public IActionEvent Modified => mModified;

    // SDK 最小订阅面（IReadOnlyNotifiableProperty）适配到富事件。
    event Action? IReadOnlyNotifiableProperty<T>.WillModified
    {
        add { if (value != null) mWillModify.Subscribe(value); }
        remove { if (value != null) mWillModify.Unsubscribe(value); }
    }
    event Action? IReadOnlyNotifiableProperty<T>.Modified
    {
        add { if (value != null) mModified.Subscribe(value); }
        remove { if (value != null) mModified.Unsubscribe(value); }
    }

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
            if (EqualityComparer<T>.Default.Equals(value, mValue))
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
