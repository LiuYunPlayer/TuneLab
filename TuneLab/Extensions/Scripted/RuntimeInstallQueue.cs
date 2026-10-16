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
    // onDone 回执带 (packageDir, ok, message)——M5c：失败原因要能透到 UI（否则按钮只是转完就沉默）。
    public static bool Schedule(string packageId, string packageDir, RuntimeSpec spec,
        Func<RuntimeSpec, Task<(bool ok, string message)>>? executor = null, Action<string, bool, string>? onDone = null)
    {
        lock (sInFlight)
        {
            if (!sInFlight.Add(packageId))
                return false;
        }
        var run = executor ?? (async s =>
        {
            var res = await ScriptedRuntimeFetcher.FetchInstallAsync(s, progress: null, CancellationToken.None).ConfigureAwait(false);
            return (res.Ok, res.Message);
        });
        var done = onDone ?? DefaultDone;
        _ = Task.Run(async () =>
        {
            bool ok = false;
            string message = "";
            try
            {
                (ok, message) = await run(spec).ConfigureAwait(false);
                Log.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture, "scripted runtime auto-fetch for {0}: {1} ({2})", packageId, ok ? "installed" : "failed", message));
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Log.Warning(string.Format(System.Globalization.CultureInfo.InvariantCulture, "scripted runtime auto-fetch for {0} threw: {1}", packageId, ex.Message));
            }
            finally
            {
                lock (sInFlight)
                    sInFlight.Remove(packageId);
            }
            try { done(packageDir, ok, message); } catch { }
        });
        return true;
    }

    static void DefaultDone(string packageDir, bool ok, string message)
    {
        if (!ok) return;   // 自动档失败：静默（下次调用再试），仅日志
        try { Dispatcher.UIThread.Post(() => ExtensionManager.Reload(packageDir)); } catch { }
    }

    // 在途查询（UI 置灰按钮用）。
    public static bool IsPending(string packageId)
    {
        lock (sInFlight) return sInFlight.Contains(packageId);
    }
}
