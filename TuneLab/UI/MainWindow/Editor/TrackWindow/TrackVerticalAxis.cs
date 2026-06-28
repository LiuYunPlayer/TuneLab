using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Animation;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.Data;
using TuneLab.Utils;

namespace TuneLab.UI;

internal class TrackVerticalAxis : AnimationScalableScrollAxis
{
    public double TrackHeight
    {
        get => Factor - 1;
        set => Factor = value + 1;
    }

    public readonly struct Position
    {
        public readonly double Y => mAxis.Pos2Coor(mFlag);
        public readonly int TrackIndex => (int)mFlag;
        public Position(TrackVerticalAxis axis, double y)
        {
            mAxis = axis;
            mFlag = axis.Coor2Pos(y);
        }

        readonly TrackVerticalAxis mAxis;
        readonly double mFlag;
    }

    public interface IDependency
    {
        IHolder<IProject> ProjectHolder { get; }
    }

    public TrackVerticalAxis(IDependency dependency)
    {
        mDependency = dependency;
        mDragAnimation = new(this);

        TrackHeight = 64;

        mDependency.ProjectHolder.When(p => p.Tracks.Modified).Subscribe(OnTracksChanged, s);
        mDependency.ProjectHolder.Modified.Subscribe(OnProjectChanged, s);

        OnProjectChanged();
    }

    ~TrackVerticalAxis()
    {
        s.DisposeAll();
    }

    // 所有按轨道索引定位的处都走此入口：非拖拽时即 Pos2Coor(index)；拖拽时被拖轨返回跟手浮动 top、
    // 其余轨返回带缓动让位后的位置。轨道头与编排区共用此源，于是“拖头时内容同步跟随”天然成立。
    public double GetTop(int index)
    {
        return GetVisualTop(index);
    }

    public double GetVisualTop(int index)
    {
        if (!mIsDragging || mAnimatedSlots == null)
            return Pos2Coor(index);

        if (mDraggedSet.Contains(index))
            return Pos2Coor(index) + mDragPixelOffset;   // 被拖轨整体平移同一像素量 → 彼此 index 差不变、不收拢

        if (index >= 0 && index < mAnimatedSlots.Length)
            return Pos2Coor(mAnimatedSlots[index]);

        return Pos2Coor(index);
    }

    public double GetBottom(int index)
    {
        return GetTop(index) + TrackHeight;
    }

    public bool IsTrackDragging => mIsDragging;
    public bool IsDraggedTrack(int index) => mIsDragging && mDraggedSet.Contains(index);
    public int DragDelta => mDragDelta;

    // 开始拖拽一组（多选）轨道：draggedIndices 为被拖轨原索引集合，primaryIndex 为抓住的那条。
    // 模型：整组平移同一个 delta，被拖轨彼此相对 index 不变（不收拢）；非拖拽轨按序填入剩余槽位。
    public void BeginTrackDrag(IReadOnlyList<int> draggedIndices, int primaryIndex, double grabOffsetY)
    {
        int count = mDependency.ProjectHolder.Value?.Tracks.Count ?? 0;
        if (draggedIndices.Count == 0 || (uint)primaryIndex >= count)
            return;

        mDraggedSorted = draggedIndices.OrderBy(i => i).ToArray();
        mDraggedSet = new HashSet<int>(mDraggedSorted);
        mPrimaryIndex = primaryIndex;
        mDragGrabOffsetY = grabOffsetY;
        mDeltaMin = -mDraggedSorted[0];                         // 使 minSel + delta ≥ 0
        mDeltaMax = (count - 1) - mDraggedSorted[^1];           // 使 maxSel + delta ≤ count-1
        mDragPixelOffset = 0;
        mDragDelta = 0;
        mAnimatedSlots = new double[count];
        for (int i = 0; i < count; i++)
            mAnimatedSlots[i] = i;
        mTargetSlots = new int[count];
        ComputeTargetSlots();   // delta=0：非拖拽轨目标即原位
        mIsDragging = true;
        mLastTickMs = double.NaN;
        AnimationManager.SharedManager.StartAnimation(mDragAnimation);
        NotifyLayoutChanged();
    }

    public void UpdateTrackDrag(double pointerYInView)
    {
        if (!mIsDragging || mAnimatedSlots == null)
            return;

        double rawOffset = (pointerYInView - mDragGrabOffsetY) - Pos2Coor(mPrimaryIndex);
        mDragPixelOffset = Math.Clamp(rawOffset, mDeltaMin * Factor, mDeltaMax * Factor);
        int newDelta = Math.Clamp((int)Math.Round(mDragPixelOffset / Factor), mDeltaMin, mDeltaMax);
        if (newDelta != mDragDelta)
        {
            mDragDelta = newDelta;
            ComputeTargetSlots();
        }
        NotifyLayoutChanged();
    }

    // 按当前 delta 算每条非拖拽轨应落的整数槽：被拖轨占走 {s+delta}，其余轨按序填入未占用槽。
    void ComputeTargetSlots()
    {
        if (mAnimatedSlots == null || mTargetSlots == null)
            return;

        int count = mAnimatedSlots.Length;
        var occupied = new bool[count];
        foreach (var s in mDraggedSorted)
        {
            int t = s + mDragDelta;
            if ((uint)t < count)
                occupied[t] = true;
        }

        int ptr = 0;
        for (int j = 0; j < count; j++)
        {
            if (mDraggedSet.Contains(j))
                continue;

            while (ptr < count && occupied[ptr]) ptr++;
            mTargetSlots[j] = ptr++;
        }
    }

    public void EndTrackDrag()
    {
        if (!mIsDragging)
            return;

        mIsDragging = false;
        AnimationManager.SharedManager.StopAnimation(mDragAnimation);
        mAnimatedSlots = null;
        mTargetSlots = null;
        mDraggedSet = new HashSet<int>();
        mDraggedSorted = Array.Empty<int>();
        mDragDelta = 0;
        mDragPixelOffset = 0;
        NotifyLayoutChanged();
    }

    int TargetSlot(int index) => mTargetSlots == null ? index : mTargetSlots[index];

    void TickDrag(double millisec)
    {
        if (mAnimatedSlots == null)
        {
            if (!mIsDragging)
                AnimationManager.SharedManager.StopAnimation(mDragAnimation);
            return;
        }

        double dt = double.IsNaN(mLastTickMs) ? mFrameMs : Math.Max(0, millisec - mLastTickMs);
        mLastTickMs = millisec;

        double factor = 1 - Math.Exp(-dt / SlotSmoothTau);
        bool moving = false;
        bool changed = false;
        for (int i = 0; i < mAnimatedSlots.Length; i++)
        {
            if (mDraggedSet.Contains(i))
                continue;

            double target = TargetSlot(i);
            double cur = mAnimatedSlots[i];
            double next = cur + (target - cur) * factor;
            if (Math.Abs(next - target) < 0.001)
                next = target;
            else
                moving = true;

            if (next != cur)
            {
                mAnimatedSlots[i] = next;
                changed = true;
            }
        }

        if (changed)
            NotifyLayoutChanged();

        if (!mIsDragging && !moving)
            AnimationManager.SharedManager.StopAnimation(mDragAnimation);
    }

    sealed class DragAnimation(TrackVerticalAxis axis) : IAnimation
    {
        public void Update(double millisec) => axis.TickDrag(millisec);
    }

    // 拖拽布局变化经基类的脏标志流程触发重布局（标脏 + gated flush），与坐标变更同一通路。
    void NotifyLayoutChanged()
    {
        SetChanged();
        TryNotify();
    }

    public Position GetPosition(double y)
    {
        return new(this, y);
    }

    public void SetAutoContentSize(bool isAuto)
    {
        mIsAutoContentSize = isAuto;
        if (isAuto)
        {
            OnTracksChanged();
        }
        else
        {
            ContentSize = int.MaxValue;
        }
    }

    void OnProjectChanged()
    {
        OnTracksChanged();
    }

    // 视图高度变化时也需重算可滚动范围（含一个视图高的余量）。
    public void RefreshContentSize() => OnTracksChanged();

    void OnTracksChanged()
    {
        if (!mIsAutoContentSize)
            return;

        var project = mDependency.ProjectHolder.Value;
        if (project == null)
            return;

        // 可滚动范围 = 轨道内容总高 + 一个视图高（以行计）：让最底下的“+”能滚到视图顶部，
        // 其下露出的空白区可点击作为取消选中区。
        double extraRows = Factor > 0 ? Math.Max(1.0, ViewLength / Factor) : 1.0;
        ContentSize = project.Tracks.Count + extraRows;
    }

    bool mIsAutoContentSize = true;

    bool mIsDragging = false;
    int[] mDraggedSorted = Array.Empty<int>();   // 被拖（选中）轨原索引，升序
    HashSet<int> mDraggedSet = new();
    int mPrimaryIndex = -1;                        // 抓住的那条轨原索引
    int mDeltaMin = 0;                             // delta 下界（使全组留在 [0,count-1]）
    int mDeltaMax = 0;                             // delta 上界
    int mDragDelta = 0;                            // 全组平移量（整数，可负）
    double mDragGrabOffsetY = 0;
    double mDragPixelOffset = 0;                   // 被拖轨连续浮动像素量（跟手）
    int[]? mTargetSlots = null;                    // 各非拖拽轨在当前 delta 下应落的整数槽
    double[]? mAnimatedSlots = null;
    double mLastTickMs = double.NaN;
    const double SlotSmoothTau = 45;   // 让位缓动时间常数（ms），越小越跟手
    const double mFrameMs = 16;
    readonly DragAnimation mDragAnimation;

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
