using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace TuneLab.Commands;

// 运行在【有界面的宿主进程】里的入口（侧栏 agent、CLI 的 attach 模式）用的现成实现：转发到 Avalonia
// 的 UI 线程。headless 入口不用它，另给一个泵驱动的实现——接口本身不依赖 Avalonia，这就是分开的意义。
internal sealed class UiThreadDispatcher : IMainThreadDispatcher
{
    public static readonly UiThreadDispatcher Instance = new();

    public async Task<T> InvokeAsync<T>(Func<T> func) => await Dispatcher.UIThread.InvokeAsync(func);
}
