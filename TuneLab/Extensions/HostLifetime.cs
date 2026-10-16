using System.Threading;

namespace TuneLab.Extensions;

// 宿主进程生命周期的取消源：应用退出时取消。交给插件的长任务（按钮点击、进度弹窗的 work）以它为根，
// 退出时一并收到取消。插件运行期不会被卸载（启停要重启才生效），故这是插件侧长任务唯一的外部取消来源。
internal static class HostLifetime
{
    public static CancellationToken Token => sShutdown.Token;

    public static void Shutdown()
    {
        if (!sShutdown.IsCancellationRequested)
            sShutdown.Cancel();
    }

    static readonly CancellationTokenSource sShutdown = new();
}
