using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

// 按登记顺序持有 IDisposable，DisposeAll 逆序销毁（后建先拆）——退订常有此期望（后订阅者依赖先订阅者、
// 须先拆）。用 List 而非 HashSet 以保序（顺带不再去重：同一 disposable 登记两次即拆两次，由调用方自负）。
// 实现 IDisposable，支持 using（作用域退出即 DisposeAll）。
public class DisposableManager : IDisposable
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
        // 逆序：后登记的先拆（对齐"后建先拆"的退订期望）。
        for (int i = mDisposables.Count - 1; i >= 0; i--)
            mDisposables[i].Dispose();
        mDisposables.Clear();
    }

    void IDisposable.Dispose() => DisposeAll();

    readonly List<IDisposable> mDisposables = new();
}
