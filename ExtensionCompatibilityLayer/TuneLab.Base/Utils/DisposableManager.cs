using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Utils;

public class DisposableManager
{
    public static DisposableManager operator +(DisposableManager manager, IDisposable disposable)
    {
        manager.Add(disposable);
        return manager;
    }

    public static DisposableManager operator -(DisposableManager manager, IDisposable disposable)
    {
        manager.Remove(disposable);
        return manager;
    }

    public void Add(IDisposable disposable)
    {
        mDisposables.Add(disposable);
    }

    public void Remove(IDisposable disposable)
    {
        mDisposables.Remove(disposable);
    }

    public void DisposeAll()
    {
        foreach (var disposable in mDisposables)
        {
            disposable.Dispose();

        }
        mDisposables.Clear();
    }

    readonly HashSet<IDisposable> mDisposables = new();
}
