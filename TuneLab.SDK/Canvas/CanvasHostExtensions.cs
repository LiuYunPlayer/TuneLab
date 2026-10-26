using System;

namespace TuneLab.SDK;

// ICanvasHost 的便利写法：只组合已有原语、不是新能力，故不进接口（接口只放原语，宿主实现也就不必各写一份）。
public static class CanvasHostExtensions
{
    // BeginEdit 的作用域写法：立即开始一次编辑，返回的句柄 Dispose 即 EndEdit——只生效一次，重复 Dispose 无副作用。
    // 跨事件的编辑（按下开始、松开结束）把句柄存进字段；同步代码里可写 using (host.EditScope()) { … }。
    public static IDisposable EditScope(this ICanvasHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.BeginEdit();
        return new Scope(host);
    }

    sealed class Scope(ICanvasHost host) : IDisposable
    {
        public void Dispose()
        {
            if (mDisposed)
                return;
            mDisposed = true;
            host.EndEdit();
        }

        bool mDisposed;
    }
}
