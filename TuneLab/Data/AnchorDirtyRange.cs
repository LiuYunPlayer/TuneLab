using System;
using System.Collections.Generic;
using System.Diagnostics;
using TuneLab.Foundation;

namespace TuneLab.Data;

/// <summary>
/// 锚点编辑的脏区累加器。曲线为 Monotonic Hermite 插值：段 [P_j, P_j+1] 的取值完全由
/// (P_j, P_j+1, m_j, m_j+1) 决定，而切线 m_j 只依赖 P_j-1..P_j+1——故动一个锚点最远波及
/// [i−2, i+2] 锚点跨度。邻居几何是时点敏感的：删除须在移除前入账、插入须在落位后入账。
///
/// 收窄（仅连续轨）：切线被钳为 0 的锚点（两侧割线异号 / 有一侧为 0 ⇒ 极值或平台端；端点亦恒 0）
/// 与其外侧邻居解耦——P_i 怎么动它都还是 0，那一侧的外区间纹丝不动，于是只需外扩到该锚点为止。
/// 更强的一档是**等值相邻**：两个等值锚点之间恒为常数（该侧割线为 0 ⇒ 两端切线皆钳零），
/// 于是那一段连自己都不脏，边界收回本点——在等值的两点之间画一小段线不再波及两点之间的全部。
/// 判据必须**变动前后都成立**，否则会漏掉"前非极值、后成极值"这类切线由非零变 0 的失效。实现上
/// 不比较两个状态：每次入账各按当时几何判定，调用方在改动前后各入账一次，并集自然等价于
/// 「前后都恒 0 才收窄」。收窄因此与相位纪律是一体的——未补齐相位的调用方必须走
/// <see cref="PiecewiseGroup"/> 的保守口径（恒 ±2，不收窄）。
///
/// 端点平延（连续轨）：首 / 末锚点之外按端点值平延至无穷。该侧是否失效**独立于上面的收窄规则**，
/// 只由端点的 (Pos, Value) 前后比较决定，见 <see cref="CloseTails"/>。分段轨组外无值，不参与。
/// </summary>
internal class AnchorDirtyRange
{
    /// 连续轨：启用切线收窄（调用方须遵守类文档的相位纪律）+ 端点平延值判定（构造即抓改动前快照，
    /// 编辑完成后调 <see cref="CloseTails"/> 收口）。
    public static AnchorDirtyRange ContinuousTrack(IReadOnlyList<AnchorPoint> anchors) => new(true, anchors);

    /// 分段轨：组外无值故无平延可谈；恒按 ±2 保守外扩、不做收窄（相位纪律未补齐，宁多勿漏）。
    public static AnchorDirtyRange PiecewiseGroup() => new(false, null);

    public double Start => mStart;
    public double End => mEnd;
    public bool IsEmpty => mStart > mEnd;

    public void Union(double start, double end)
    {
        mStart = Math.Min(mStart, start);
        mEnd = Math.Max(mEnd, end);
    }

    /// 锚点 index 自身的 (Pos, Value) 变了（增 / 删 / 改）：其两个邻接区间失效，且左右邻的切线
    /// 随之改变、再各失效一个外区间 → 外扩两个锚点；邻居切线恒 0 时那一侧与本锚点解耦，收窄到该邻居。
    public void Touch(IReadOnlyList<AnchorPoint> anchors, int index)
    {
        Debug.Assert((uint)index < anchors.Count, "Touched anchor index out of range.");
        int last = anchors.Count - 1;
        Union(anchors[Math.Max(BoundIndex(anchors, index, -1), 0)].Pos,
              anchors[Math.Min(BoundIndex(anchors, index, 1), last)].Pos);
    }

    // 该侧脏区边界落在哪个锚点上（step = ∓1）：
    //   ① 邻居与本点**等值** ⇒ 两者之间是常数段——等值意味着该侧割线为 0，于是两端切线都被钳零，
    //      段内恒等于该值，本点怎么动都传不进去 ⇒ 边界收回本点自己，那一段一寸都不脏。
    //   ② 邻居切线恒 0（极值 / 平台端 / 端点）⇒ 与本点解耦，其外的区间不变 ⇒ 边界停在邻居。
    //   ③ 否则邻居的切线随本点改变，其外一段跟着失效 ⇒ 再走一个。
    // ①②都靠前后两相位取并兜底：判据只按当时几何成立，某一相位不满足就自动退回更宽的档位。
    int BoundIndex(IReadOnlyList<AnchorPoint> anchors, int index, int step)
    {
        int neighbor = index + step;
        if (!mNarrow || (uint)neighbor >= (uint)anchors.Count)
            return index + 2 * step;

        if (anchors[neighbor].Value == anchors[index].Value)
            return index;

        return SlopeIsZero(anchors, neighbor) ? neighbor : index + 2 * step;
    }

    /// 一处邻接关系变化（插入前的落位缝 / 删除后的合拢缝），index = 缝左侧锚点下标（−1 = 缝在首锚点之前）：
    /// 缝两侧锚点自身未动、只有切线因邻居易主而变，故各只失效自己的两个邻接区间 → 外扩一个锚点。
    /// 当前切线已恒 0 的锚点跳过：它若在改动后仍恒 0 便根本没变；若变为非 0，对侧相位的收窄判定
    /// 必然覆盖到它之外——两种情况都不欠这一笔。
    public void TouchGap(IReadOnlyList<AnchorPoint> anchors, int index)
    {
        TouchSlopeChange(anchors, index);
        TouchSlopeChange(anchors, index + 1);
    }

    /// 编辑完成后收口端点平延（连续轨；分段轨为空操作）。值变 ⇒ 整侧到无穷（新旧两段平延之并）；
    /// 仅位置变 ⇒ 只脏位移覆盖的那一段。无锚点时曲线全域取 0，等价于「平延值 0」，故空 ↔ 非空的
    /// 转换也落在同一判据里：值相同就真的处处相同，一寸都不必报脏。
    public void CloseTails(IReadOnlyList<AnchorPoint> anchors)
    {
        if (!mNarrow)
            return;

        var after = Tails(anchors);
        if (after.HeadValue != mTailsBefore.HeadValue)
            Union(double.NegativeInfinity, Math.Max(mTailsBefore.HeadPos, after.HeadPos));
        else if (mTailsBefore.HasAnchors && after.HasAnchors && mTailsBefore.HeadPos != after.HeadPos)
            Union(Math.Min(mTailsBefore.HeadPos, after.HeadPos), Math.Max(mTailsBefore.HeadPos, after.HeadPos));

        if (after.TailValue != mTailsBefore.TailValue)
            Union(Math.Min(mTailsBefore.TailPos, after.TailPos), double.PositiveInfinity);
        else if (mTailsBefore.HasAnchors && after.HasAnchors && mTailsBefore.TailPos != after.TailPos)
            Union(Math.Min(mTailsBefore.TailPos, after.TailPos), Math.Max(mTailsBefore.TailPos, after.TailPos));
    }

    void TouchSlopeChange(IReadOnlyList<AnchorPoint> anchors, int index)
    {
        if ((uint)index >= (uint)anchors.Count || SlopeIsZero(anchors, index))
            return;

        int last = anchors.Count - 1;
        Union(anchors[Math.Max(index - 1, 0)].Pos, anchors[Math.Min(index + 1, last)].Pos);
    }

    // 锚点 index 的切线是否恒 0。越界与端点一并算 0：端点切线本就恒 0，越界侧则没有更外的区间可脏。
    static bool SlopeIsZero(IReadOnlyList<AnchorPoint> anchors, int index)
    {
        if (index <= 0 || index >= anchors.Count - 1)
            return true;

        var point = anchors[index].ToPoint();
        return MathUtility.MonotonicHermiteSlopeIsZero(
            MathUtility.Slope(point, anchors[index - 1].ToPoint()),
            MathUtility.Slope(point, anchors[index + 1].ToPoint()));
    }

    // 两端平延的判据快照。空轨记 (0, ∓∞)：全域取 0 即等价于两侧平延值都是 0，位置退化到无穷远。
    static TailSnapshot Tails(IReadOnlyList<AnchorPoint> anchors)
    {
        if (anchors.Count == 0)
            return new(false, 0, double.NegativeInfinity, 0, double.PositiveInfinity);

        return new(true, anchors[0].Value, anchors[0].Pos, anchors[anchors.Count - 1].Value, anchors[anchors.Count - 1].Pos);
    }

    AnchorDirtyRange(bool narrow, IReadOnlyList<AnchorPoint>? anchors)
    {
        mNarrow = narrow;
        mTailsBefore = anchors == null ? default : Tails(anchors);
    }

    readonly record struct TailSnapshot(bool HasAnchors, double HeadValue, double HeadPos, double TailValue, double TailPos);

    readonly bool mNarrow;
    readonly TailSnapshot mTailsBefore;
    double mStart = double.PositiveInfinity;
    double mEnd = double.NegativeInfinity;
}
