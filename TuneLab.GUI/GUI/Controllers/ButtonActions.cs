using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.GUI.Controllers;

// 一棵属性面板树（根 PropertyObjectController + 其嵌套的对象 / 数组 / 列表控制器）共享的按钮信息：
// · Scope：宿主为根面板设的作用域串（如 "settings/<包>/<扩展>"、"note/<part>/<选中 note>"），与按钮在树里的位置拼成
//   正在执行的点击的键——面板重建后（切 tab 再切回、重选同一批 note）仍认得出"同一个按钮还在执行"、继续置灰。
//   宿主没设时给每棵树一个唯一串：隔离正确，只是重建后认不回来。
// · ActionCompleted：本树里任一按钮执行结束（无论成败）——面板持有方据此重新询问声明、刷新面板。
internal sealed class ButtonRoot
{
    public string Scope { get; set; } = "#" + Interlocked.Increment(ref sNext);
    public event Action? ActionCompleted;
    internal void RaiseCompleted() => ActionCompleted?.Invoke();

    static long sNext;
}

// 按钮点击的统一调度（GUI 侧，只在 UI 线程使用）：登记执行中、防重入、结束后通知所在面板。
// 真正执行（来源插件归属、本次点击的弹窗服务、异常提示）由宿主经 Invoke 注入——GUI 层不认识插件。
internal static class ButtonActions
{
    // 宿主启动时替换。参数 = 按钮 config + 按钮所在的窗口（弹窗服务的父窗口）。默认实现（测试 / 无宿主环境）
    // 给一个无窗口口径的弹窗服务。实现须自己吞掉异常。
    public static Func<ButtonConfig, Window?, Task> Invoke { get; set; } = (config, _) => config.OnClick(NoWindowClick.Instance);

    // 执行中集合变化（按钮据此刷新置灰态）。
    public static event Action? RunningChanged;

    public static bool IsRunning(string key) => sRunning.Contains(key);

    public static async void Click(string key, ButtonConfig config, ButtonRoot root, Window? owner)
    {
        if (!sRunning.Add(key))
            return;   // 同一按钮还在执行：不重入
        RunningChanged?.Invoke();
        try
        {
            await Invoke(config, owner);
        }
        catch (Exception ex)
        {
            Log.Error("Button action escaped the host invoker: " + ex);   // 宿主实现应已吞掉；防御
        }
        finally
        {
            sRunning.Remove(key);
            RunningChanged?.Invoke();
            root.RaiseCompleted();
        }
    }

    static readonly HashSet<string> sRunning = new();

    // 无窗口口径：提示无声、确认 = false、进度直接执行 work、输入 = null。
    sealed class NoWindowClick : IButtonClickContext, IDialogService, IProgressReporter
    {
        public static readonly NoWindowClick Instance = new();
        public IDialogService Dialogs => this;
        public Task ShowMessage(string title, string message) => Task.CompletedTask;
        public Task<bool> Confirm(string title, string message) => Task.FromResult(false);
        public Task RunWithProgress(string title, Func<IProgressReporter, CancellationToken, Task> work) => work(this, CancellationToken.None);
        public Task<PropertyObject?> PromptInputs(string title, ObjectConfig config) => Task.FromResult<PropertyObject?>(null);
        public void Report(double? fraction, string? status = null) { }
        public void SetCancellable(bool cancellable) { }
    }
}
