using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;

namespace TuneLab.GUI;

/// <summary>
/// 窗口级"编辑中"闸门：窗口里有值控件正处于一次进行中的编辑（拖动 / 键入：从开始改值到提交）时，
/// 这个窗口里的属性面板都推迟刷新，编辑结束后再刷。
/// </summary>
/// <remarks>
/// 为什么要推迟：编辑进行中按新声明刷新面板，会把正被拖的控件从中途拆掉（声明去掉了它 / 整面板重建），
/// 或者让它上方的字段增减、把它挤得跳位置。声明失效随时可能到来（插件后台分析、按钮动作完成、合成结果……），
/// 用户控制不了时机。推迟不丢信息：声明是对当前状态的纯查询，期间的多次失效合并成编辑结束后的一次刷新。
///
/// 为什么按窗口：同一窗口里的面板共处一块布局（如侧栏里音符面板在上、音素面板在下），任一处变动都能让别处移位；
/// 跨窗口互不影响。也不能放大到全局——焦点按窗口保存，别的窗口里一个没失焦的文本框会一直算"编辑中"。
///
/// 两种接入：走 Reconcile 的面板由 PropertyObjectController 自己推迟（见其 Reconcile）；整面板重建的持有方
/// 用 ViewRefreshScheduler.SuspendWhileEditing 在编辑中挂起自己的调度器。仅限 UI 线程使用。
/// </remarks>
internal sealed class EditGate
{
    /// <summary>visual 所在窗口的闸门；不在可视树里（不可见、也就无从编辑）时为 null。</summary>
    public static EditGate? Of(Visual? visual)
        => visual != null && TopLevel.GetTopLevel(visual) is { } top ? sGates.GetValue(top, _ => new EditGate()) : null;

    public bool IsEditing => mEditing > 0;

    /// <summary>进入 / 退出编辑中（边沿触发：只在 IsEditing 翻转时发）。</summary>
    public IActionEvent EditingChanged => mEditingChanged;

    /// <summary>开始一次编辑，返回的句柄 Dispose 即结束（幂等）。各控件的每次编辑各持一个句柄，全部结束才算退出编辑中。</summary>
    public IDisposable BeginEdit()
    {
        if (mEditing++ == 0)
            mEditingChanged.Invoke();
        return new EditToken(this);
    }

    /// <summary>
    /// 编辑结束后执行一次（不在编辑中则下一拍执行）。推迟到下一拍：结束编辑的那次提交会同步引发数据通知、
    /// 持有方据此排队重新声明——让它们先落定，再跑推迟的工作。
    /// </summary>
    public void AfterEditing(Action action)
    {
        mAfterEditing.Add(action);
        if (!IsEditing)
            ScheduleAfterEditing();
    }

    /// <summary>
    /// 把一个值控件的编辑过程（ValueWillChange → ValueCommitted）登记到它所在窗口的闸门——与 BindDataProperty 的 merge
    /// 同一对边界：拖拽类控件 = 按下 / 开拖 → 松手，文本类 = 获得焦点 → 失焦 / 回车。窗口在开始编辑时现取（控件可能晚于
    /// 绑定才进可视树）。控件在编辑中途被释放（context 回收）或离开可视树时也结束登记，不让窗口永远挂起。控件须是 Visual。
    /// </summary>
    public static void Track<T>(IDataValueController<T> controller, DisposableManager context)
    {
        Debug.Assert(controller is Visual, "EditGate.Track needs a control to find its window.");
        context.Add(new EditTracking<T>(controller));
    }

    void EndEdit()
    {
        if (--mEditing > 0)
            return;
        mEditingChanged.Invoke();
        if (mAfterEditing.Count > 0)
            ScheduleAfterEditing();
    }

    void ScheduleAfterEditing()
    {
        if (mAfterEditingScheduled)
            return;
        mAfterEditingScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            mAfterEditingScheduled = false;
            if (IsEditing)
                return;   // 这一拍里又开始了新的编辑：留到它结束
            var actions = mAfterEditing.ToArray();
            mAfterEditing.Clear();
            foreach (var action in actions)
                action();
        });
    }

    sealed class EditToken(EditGate gate) : IDisposable
    {
        public void Dispose()
        {
            if (mDisposed)
                return;
            mDisposed = true;
            gate.EndEdit();
        }

        bool mDisposed;
    }

    sealed class EditTracking<T> : IDisposable
    {
        public EditTracking(IDataValueController<T> controller)
        {
            mController = controller;
            controller.ValueWillChange.Subscribe(OnWillChange);
            controller.ValueCommitted.Subscribe(End);
            // 控件离开可视树（面板被整体丢弃而没逐个释放、窗口关闭……）也结束登记：一个永远等不到提交的句柄
            // 会让整个窗口的面板永远不刷新，这里兜底，不依赖各处释放纪律。
            if (controller is Visual visual)
                visual.DetachedFromVisualTree += OnDetached;
        }

        void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => End();

        // 一次编辑里可能多次 WillChange（如数值框开拖），只登记一次。
        void OnWillChange() => mToken ??= Of(mController as Visual)?.BeginEdit();

        void End()
        {
            mToken?.Dispose();
            mToken = null;
        }

        public void Dispose()
        {
            mController.ValueWillChange.Unsubscribe(OnWillChange);
            mController.ValueCommitted.Unsubscribe(End);
            if (mController is Visual visual)
                visual.DetachedFromVisualTree -= OnDetached;
            End();
        }

        readonly IDataValueController<T> mController;
        IDisposable? mToken;
    }

    int mEditing;
    bool mAfterEditingScheduled;
    readonly List<Action> mAfterEditing = new();
    readonly ActionEvent mEditingChanged = new();

    static readonly ConditionalWeakTable<TopLevel, EditGate> sGates = new();
}
