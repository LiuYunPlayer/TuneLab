using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using TuneLab.Foundation;

namespace TuneLab.GUI;

/// <summary>
/// 视图"标脏 → 合拍重建"调度器：把一拍内的多次数据变化合并成一次重建/刷新，取代各处手拼的
/// "pending 标志 + Dispatcher.Post" 与 "DirtyHandler + 手写接线"。实例归控件自持，按需声明。
/// </summary>
/// <remarks>
/// 使用纪律：订阅回调里只调 Invalidate*、不读数据——所有读取集中在 rebuild / refreshValues 回调内。
/// 它们总在引发事件的数据操作整体结束后的下一拍执行，读到的是 settled 状态，因此"按旧结构快照
/// （下标、钉死态等）读已变化的数据"这类越界窗口在结构上不存在，无需在各订阅点写结构守卫。
///
/// 语义要点：
/// - 边沿触发：首次标脏排队一次 flush，脏期间的后续标脏被吸收；空闲时零开销（不做每帧轮询）。
/// - 两级脏位：Structure（结构变化 → rebuild，重建天然刷值、连带清 Values）高于 Values（仅值变化
///   → refreshValues）。"仅刷值"只在本拍结构干净时执行，故值回调按构建时结构读数据是安全的——
///   任何结构变化必然已在同一拍把 Structure 位标上。
/// - 先清后跑：flush 先复位排队标志与脏位、再调回调。回调抛异常不会把调度器锁死（脏位已清，
///   下个数据事件正常再触发）；回调执行中若又标脏则排下一拍（宁多建一次，不丢刷新）。
/// - SuspendWhileEditing：所在窗口编辑进行中抑制（拖拽/键入期间避免重建打断交互，见 EditGate）。flush 跳过
///   但保留脏位，编辑结束时若有脏立即补排。
///
/// 仅限 UI 线程使用（数据层事件已由各处 marshal 回 UI 线程）。
/// </remarks>
public class ViewRefreshScheduler(Action rebuild, Action? refreshValues = null) : IDisposable
{
    /// <summary>是否挂起中（见 SuspendWhileEditing）。</summary>
    public bool Suspended => mSuspendCount > 0;

    // 挂起句柄：持有期间 flush 保留脏位直接跳过；最后一个句柄释放（Dispose，幂等）时若有脏立即补排一拍。
    IDisposable Suspend()
    {
        mSuspendCount++;
        return new SuspendToken(this);
    }

    void Resume()
    {
        if (--mSuspendCount == 0 && (mStructureDirty || mValuesDirty))
            Schedule();
    }

    /// <summary>
    /// anchor 所在窗口有编辑进行中时挂起本调度器（见 EditGate），编辑结束即释放。anchor 进出可视树时跟着换窗口。
    /// 整面板重建的持有方用它：不在用户拖动 / 键入的中途重建面板。context 回收时一并释放。
    /// </summary>
    public void SuspendWhileEditing(Visual anchor, DisposableManager context)
        => context.Add(new EditingSuspension(this, anchor));

    sealed class EditingSuspension : IDisposable
    {
        public EditingSuspension(ViewRefreshScheduler scheduler, Visual anchor)
        {
            mScheduler = scheduler;
            mAnchor = anchor;
            anchor.AttachedToVisualTree += OnAttachmentChanged;
            anchor.DetachedFromVisualTree += OnAttachmentChanged;
            Bind(EditGate.Of(anchor));
        }

        void OnAttachmentChanged(object? sender, VisualTreeAttachmentEventArgs e) => Bind(EditGate.Of(mAnchor));

        void Bind(EditGate? gate)
        {
            if (gate == mGate)
                return;
            mGate?.EditingChanged.Unsubscribe(Sync);
            mGate = gate;
            mGate?.EditingChanged.Subscribe(Sync);
            Sync();
        }

        void Sync()
        {
            if (mGate?.IsEditing == true)
            {
                mSuspension ??= mScheduler.Suspend();
                return;
            }
            mSuspension?.Dispose();
            mSuspension = null;
        }

        public void Dispose()
        {
            mAnchor.AttachedToVisualTree -= OnAttachmentChanged;
            mAnchor.DetachedFromVisualTree -= OnAttachmentChanged;
            Bind(null);
        }

        readonly ViewRefreshScheduler mScheduler;
        readonly Visual mAnchor;
        EditGate? mGate;
        IDisposable? mSuspension;
    }

    sealed class SuspendToken(ViewRefreshScheduler scheduler) : IDisposable
    {
        public void Dispose()
        {
            if (mDisposed)
                return;
            mDisposed = true;
            scheduler.Resume();
        }

        bool mDisposed;
    }

    /// <summary>结构可能变了（成员增删、签名变化、选中集变化等）：下一拍整体重建。</summary>
    public void InvalidateStructure()
    {
        mStructureDirty = true;
        Schedule();
    }

    /// <summary>仅显示值变了：下一拍轻刷新。未提供 refreshValues 的单级用法下等价于 InvalidateStructure。</summary>
    public void InvalidateValues()
    {
        if (refreshValues == null)
        {
            InvalidateStructure();
            return;
        }

        mValuesDirty = true;
        Schedule();
    }

    /// <summary>销毁后已排队的 flush 成为空操作，后续标脏不再排队。</summary>
    public void Dispose() => mDisposed = true;

    void Schedule()
    {
        Debug.Assert(Dispatcher.UIThread.CheckAccess(), "ViewRefreshScheduler is UI-thread only.");
        if (mScheduled || mDisposed)
            return;

        mScheduled = true;
        Dispatcher.UIThread.Post(Flush);
    }

    void Flush()
    {
        mScheduled = false;                 // 先复位：flush 期间的新标脏正常排下一拍
        if (mDisposed || Suspended)
            return;                         // 挂起中保留脏位，最后一个句柄释放时补排

        if (mStructureDirty)
        {
            mStructureDirty = false;
            mValuesDirty = false;           // 重建天然刷值
            rebuild();
        }
        else if (mValuesDirty)
        {
            mValuesDirty = false;
            refreshValues!();
        }
    }

    bool mScheduled;
    bool mStructureDirty;
    bool mValuesDirty;
    int mSuspendCount;
    bool mDisposed;
}
