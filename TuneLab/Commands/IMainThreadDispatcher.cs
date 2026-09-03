using System;
using System.Threading.Tasks;

namespace TuneLab.Commands;

// 把一段活儿送到宿主主线程上跑。
//
// 为什么要抽象：数据层的改动、以及部分只读枚举（音频驱动/设备、系统字体——它们要问引擎和字体管理器）
// 都要求在宿主主线程上执行。但命令面不能直接用 Avalonia 的 Dispatcher：headless 进程里根本没有
// UI 线程，那条路会挂（见 docs/command-surface.md §8）。故由入口注入自己的实现——
// 有 UI 的进程给 UiThreadDispatcher，headless 给可泵的等价物（复用探测沙箱那套 pump）。
internal interface IMainThreadDispatcher
{
    Task<T> InvokeAsync<T>(Func<T> func);
}

internal static class MainThreadExtensions
{
    // 没有注入调度器时就地执行——无 UI 的测试进程走这条，不必为每个测试造一个假 dispatcher。
    public static Task<T> OnMainThread<T>(this CommandContext ctx, Func<T> func)
        => ctx.MainThread?.InvokeAsync(func) ?? Task.FromResult(func());
}
