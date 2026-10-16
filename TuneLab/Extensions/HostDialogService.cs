using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.I18N;
using TuneLab.UI;
using TuneLab.SDK;
using HostLog = TuneLab.Foundation.Log;

namespace TuneLab.Extensions;

// IDialogService 的宿主实现：每次按钮点击一个实例（见 PluginButtonInvoker），只在那次点击执行期间有效——
// 点击结束后 Invalidate，再调即抛（插件把服务存起来事后再用是 bug，要让它在开发期就炸响，而不是静默失效）。
// 父窗口 = 按钮所在的窗口（宿主确知点击来源，不猜）；弹窗标题后缀标明来源插件。
// owner 为 null（窗口已关 / 测试环境）时按无窗口降级：提示只写日志、确认 = false、进度直接执行 work、输入 = null。
//
// 刻意不排队：插件常在进度 work 里再弹确认 / 提示（嵌套），排队会让内层等外层关闭——死锁。
// 嵌套的弹窗以外层弹窗为父（ShowDialog 期间它就是最前面的那个），叠放自然正确。
internal sealed class HostDialogService(string source, Window? owner) : IDialogService
{
    public Task ShowMessage(string title, string message)
    {
        EnsureValid();
        HostLog.Info("[" + source + "] " + title + ": " + message);
        return OnUIThread(async () =>
        {
            if (Owner() is not { } parent)
                return;
            var dialog = new Dialog();
            dialog.SetTitle(DecorateTitle(title));
            dialog.SetMessage(message);
            dialog.AddButton("OK".Tr(TC.Dialog), Dialog.ButtonType.Primary);
            await dialog.ShowDialog(parent);
        });
    }

    public Task<bool> Confirm(string title, string message)
    {
        EnsureValid();
        return OnUIThread(async () =>
        {
            if (Owner() is not { } parent)
                return false;
            bool confirmed = false;
            var dialog = new Dialog();
            dialog.SetTitle(DecorateTitle(title));
            dialog.SetMessage(message);
            dialog.AddButton("Cancel".Tr(TC.Dialog), Dialog.ButtonType.Normal);
            // AddButton 已先挂了 Close：Close 会同步完成 ShowDialog，await 的续体会在 confirmed 置位之前就跑完——
            // 恒返回 false。故摘掉它，改为先置结果、再关窗。
            var ok = dialog.AddButton("OK".Tr(TC.Dialog), Dialog.ButtonType.Primary);
            ok.Clicked -= dialog.Close;
            ok.Clicked += () => { confirmed = true; dialog.Close(); };
            await dialog.ShowDialog(parent);
            return confirmed;
        });
    }

    public Task RunWithProgress(string title, Func<IProgressReporter, CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        EnsureValid();
        return OnUIThread(async () =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(HostLifetime.Token);
            if (Owner() is not { } parent)
            {
                await work(NullReporter.Instance, cancellation.Token);
                return;
            }

            var window = new ProgressWindow();
            window.SetTitle(DecorateTitle(title));
            window.SetProgress(0);   // 起始 0%；只有 work 报 null 才切成不确定进度（滚动条）
            window.ShowCancel("Cancel".Tr(TC.Dialog));
            window.CancelRequested += cancellation.Cancel;

            var reporter = new ProgressReporter(window);
            Task? workTask = null;
            window.Opened += (_, _) => workTask = RunAndClose();
            await window.ShowDialog(parent);
            if (workTask != null)
                await workTask;   // work 的异常原样传回调用方

            async Task RunAndClose()
            {
                try
                {
                    await work(reporter, cancellation.Token);
                }
                finally
                {
                    reporter.Detach();
                    window.Close();
                }
            }
        });
    }

    public Task<PropertyObject?> PromptInputs(string title, ObjectConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        EnsureValid();
        return OnUIThread(async () =>
        {
            if (Owner() is not { } parent)
                return null;
            // 与脚本工具的 inputs 同一个表单窗；静态表单（schema 不随值重算）。
            var window = new PropertyFormWindow(DecorateTitle(title), config, _ => (config, null), PropertyObject.Empty);
            var sparse = await window.ShowDialog<PropertyObject?>(parent);
            return sparse == null ? null : Complete(config, sparse);
        });
    }

    // 表单交回的是稀疏值（只含用户改过的键）；契约给插件完整值：以 config 声明的默认值为底，逐层叠上用户改过的。
    internal static PropertyObject Complete(ObjectConfig config, PropertyObject sparse)
        => config.GetDefaultValue().ToObject(out var defaults) ? Overlay(defaults, sparse) : sparse;

    static PropertyObject Overlay(PropertyObject baseline, PropertyObject values)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var kvp in baseline.Map)
            map.Add(kvp.Key, kvp.Value);
        foreach (var kvp in values.Map)
        {
            if (map.TryGetValue(kvp.Key, out var current) && current.ToObject(out var currentObject) && kvp.Value.ToObject(out var valueObject))
                map[kvp.Key] = PropertyValue.Create(Overlay(currentObject, valueObject));
            else
                map[kvp.Key] = kvp.Value;
        }
        return new PropertyObject(map);
    }

    // 点击结束：此后再调即抛。
    public void Invalidate() => mValid = false;

    void EnsureValid()
    {
        if (!mValid)
            throw new InvalidOperationException("The dialog service is only usable while its button click is running; it was used after the click finished.");
    }

    // 父窗口：按钮所在窗口；它已关闭（点击期间用户关了设置窗）则无窗口可挂。仅 UI 线程。
    Window? Owner() => owner is { IsVisible: true } ? owner : null;

    static Task OnUIThread(Func<Task> action)
        => Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);

    static Task<T> OnUIThread<T>(Func<Task<T>> action)
        => Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);

    string DecorateTitle(string title) => source == "host" ? title : title + " — " + source;

    volatile bool mValid = true;

    // 进度汇报：任意线程、可高频；只记最新值，每帧最多 Post 一次到 UI 线程刷新。窗口关闭后（Detach）丢弃。
    sealed class ProgressReporter(ProgressWindow window) : IProgressReporter
    {
        public void Report(double? fraction, string? status = null)
        {
            lock (mLock)
            {
                if (mDetached)
                    return;
                mFraction = fraction;
                if (status != null)
                    mStatus = status;
                if (mPending)
                    return;
                mPending = true;
            }
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Render);
        }

        public void SetCancellable(bool cancellable)
        {
            lock (mLock)
            {
                if (mDetached)
                    return;
                mCancellable = cancellable;
                if (mPending)
                    return;
                mPending = true;
            }
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Render);
        }

        public void Detach()
        {
            lock (mLock)
                mDetached = true;
        }

        void Flush()
        {
            double? fraction;
            string? status;
            bool cancellable;
            lock (mLock)
            {
                mPending = false;
                if (mDetached)
                    return;
                fraction = mFraction;
                status = mStatus;
                cancellable = mCancellable;
            }
            window.SetCancelEnabled(cancellable);
            if (fraction is { } f)
                window.SetProgress(Math.Clamp(f, 0, 1));
            else
                window.SetIndeterminate();
            if (status != null)
                window.SetStatus(status);
        }

        readonly object mLock = new();
        double? mFraction = 0;   // 与窗口起始的 0% 一致：只改文字 / 可取消性时不会把进度条刷成别的
        string? mStatus;
        bool mCancellable = true;
        bool mPending;
        bool mDetached;
    }

    sealed class NullReporter : IProgressReporter
    {
        public static readonly NullReporter Instance = new();
        public void Report(double? fraction, string? status = null) { }
        public void SetCancellable(bool cancellable) { }
    }
}
