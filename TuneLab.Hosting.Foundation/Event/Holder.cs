using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// IHolder 的可写实现：持有并替换 T，替换时发 WillModify / Modified。
public class Holder<T> : IHolder<T> where T : class
{
    public IActionEvent WillModify => mWillModify;
    public IActionEvent Modified => mModified;
    public T? Value
    {
        get => mValue;
        set => Set(value);
    }

    public static implicit operator T?(Holder<T> holder)
    {
        return holder.Value;
    }

    public void Set(T? newValue)
    {
        if (mValue == newValue)
            return;

        mWillModify.Invoke();
        mValue = newValue;
        mModified.Invoke();
    }

    T? mValue;
    readonly ActionEvent mWillModify = new();
    readonly ActionEvent mModified = new();
}
