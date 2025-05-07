namespace TuneLab.Foundation.Utils;

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
        for (int i = mDisposables.Count - 1; i >= 0; i--)
        {
            mDisposables[i].Dispose();
            mDisposables.RemoveAt(i);
        }
    }
    
    readonly List<IDisposable> mDisposables = [];
}
