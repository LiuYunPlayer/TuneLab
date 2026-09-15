using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Foundation;
using TuneLab.Utils;

namespace TuneLab.Extensions.Scripted;

// 运行时自动部署的调度环（设计 §8.6 半自动/全自动两档的共同后端）：
// 同一包只排一次在途（dedup by packageId）；成功后回 UI 线程 Reload 该包——条目循环重跑，
// ③ 级探测随即命中刚落地的 runtime 包。失败只记日志（侧栏仍显示原因 + 手动按钮兜底）。
internal static class RuntimeInstallQueue
{
    static readonly HashSet<string> sInFlight = new(StringComparer.Ordinal);

    // executor 为测试注入点（单测不碰真网络）；生产走 ScriptedRuntimeFetcher。
    public static bool Schedule(string packageId, string packageDir, RuntimeSpec spec,
        Func<RuntimeSpec, Task<bool>>? executor = null, Action<string>? onDone = null)
    {
        lock (sInFlight)
        {
            if (!sInFlight.Add(packageId))
                return false;
        }
        var run = executor ?? (async s => (await ScriptedRuntimeFetcher.FetchInstallAsync(s, progress: null, CancellationToken.None)).Ok);
        var done = onDone ?? DefaultDone;
        _ = Task.Run(async () =>
        {
            bool ok = false;
            try
            {
                ok = await run(spec).ConfigureAwait(false);
                Log.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture, "scripted runtime auto-fetch for {0}: {1}", packageId, ok ? "installed" : "failed"));
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(System.Globalization.CultureInfo.InvariantCulture, "scripted runtime auto-fetch for {0} threw: {1}", packageId, ex.Message));
            }
            finally
            {
                lock (sInFlight)
                    sInFlight.Remove(packageId);
            }
            try { done(packageDir); } catch { }
        });
        return true;
    }

    static void DefaultDone(string packageDir)
    {
        try { Dispatcher.UIThread.Post(() => ExtensionManager.Reload(packageDir)); } catch { }
    }

    // 在途查询（UI 置灰按钮用）。
    public static bool IsPending(string packageId)
    {
        lock (sInFlight) return sInFlight.Contains(packageId);
    }
}
