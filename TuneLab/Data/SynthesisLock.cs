using System;
using System.Collections.Generic;
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// 合成产物 → 用户数据的固定（lock）：把只读产物在给定 tick 区间内按真实数值写成用户曲线的锚点，一次可撤销命令。
// 两个载体同族、共用本文件的换算 / 裁剪 / 简化：参数回显 → 同 Id 可编辑轨、合成音高 → Pitch。
//
// **与音素钉死同一范式**（INote.LockPhonemes：合成音素 → 归用户的钉死音素）：引擎产物恒只读（真相源在引擎的
// 失效依赖图里），用户编辑恒落在数据层（undo / 持久 / merge 全走既有机制），固定是二者之间唯一的显式桥——
// 固定后那份数据归用户、可继续改、引擎不再覆盖它。中文术语在音素侧沿用"钉死"、在曲线侧叫"固定"，指同一件事。
//
// 它是"抓住模型这根线只改中间一段"的落地手段：没有它，用户在未覆盖段落笔就是从空白起步、模型细节全丢。
//
// 刻意只做显式一次性动作、不做持续同步：自动跟随会形成 用户覆盖 → 重合成 → 回显变 → 再写入 的反馈环，
// 且把合成结果混进 undo 栈（撤销栈里出现用户没做过的变更）。
//
// 值按真实数值原样写入、不按目标轨量程钳位——钳位会静默改数据；量程只定义值轴形状（见 AutomationConfig.Scale），
// 越界如实。回显轨与编辑轨各按自己的量程绘制、宿主不要求二者一致，声明不一致的后果就是显示错位与固定值出界。
//
// 时间：产物在全局秒域、automation 锚点在 part-local tick 域，故经 TempoManager 换算，换算即固化——
// 固定后改 tempo 不再跟随模型输出（与用户手画的曲线同待遇）。**入参 tick 是全局 tick**（与选区 / TickAxis 同域）。
internal static class SynthesisLock
{
    // 全轨作用域（不裁剪；Clip 对 ±∞ 原样返回）。当前 UI 入口都是有界范围（笔刷刷过的区间、颤音覆盖区），
    // 这对常量留给将来的整轨入口——脚本面把固定暴露成动作时正需要它。
    public const double WholeTrackStart = double.NegativeInfinity;
    public const double WholeTrackEnd = double.PositiveInfinity;

    // 简化口径与手绘一致（Simplify 的容差是斜率比、与量程无关，故各轨通用）。
    const double SimplifyGap = 5;
    const double SimplifyTolerance = 2;

    // —— 参数回显 → 同 Id 可编辑轨 ——

    // 该 key 是否配对（同 Id 既声明了可编辑轨、又声明了回显轨）。固定笔刷据此决定是否进入操作态。
    public static bool HasPairedReadback(this IMidiPart part, AutomationKey key)
    {
        if (key.IsLane)
            return false;

        if (!part.IsEffectiveAutomation(key) && !part.IsEffectivePiecewiseAutomation(key))
            return false;

        return GetReadbackConfigs(part, key)?.ContainsKey(key.Id) ?? false;
    }

    // 冻结当前回显产物引用，供笔刷式操作在 Down 时取一次、整笔复用。
    // **必须冻结**：笔刷第一帧写入就会触发合成失效、引擎可能随即清掉该区回显，后续帧再读实时产物就是空的，
    // 整笔会什么都写不进去。产物按 SDK 契约「发布即不可变、变化时换引用」，故持旧引用安全。
    //（区间失效本身已纳入批量括号、拖动期不转发，见 VoiceSynthesisContext.FlushPendingRanges；本冻结是第二重防御。）
    public static IReadOnlyList<IReadOnlyList<Point>>? CaptureReadback(this IMidiPart part, AutomationKey key)
    {
        return GetReadbackSegments(part, key);
    }

    // 裸写版（不含事务）：供笔刷式操作复用——拖动期每帧 DiscardTo(head) 后按新范围重写，抬手才 Commit。
    // 调用方负责 BeginMergeDirty / EndMergeDirty / Commit，并传入 Down 时冻结的 segments（见 CaptureReadback）。
    public static void WriteReadbackLock(this IMidiPart part, AutomationKey key, IReadOnlyList<IReadOnlyList<Point>>? segments, double startTick, double endTick,
                                        bool subtractVibrato = true)
    {
        if (segments == null)
            return;

        double extension = Settings.ParameterBoundaryExtension;
        if (part.IsEffectivePiecewiseAutomation(key))
        {
            // 分段轨的 vibrato **只作用于有值段**，故扣减也只能在原本有值处进行：那些位置引擎读到的是
            // 「曲线 + 偏移」、回显含偏移；而原本 NaN 处引擎用的是自己的模型值、回显**不含**偏移，
            // 若一并扣就会把曲线整体压低一个颤音幅度。原曲线要在写入前取（写完就有值了）。
            //
            // subtractVibrato=false 用于**空隙填充**（LockReadbackGaps）：那一整段在固定前都是 NaN，
            // 回显整段不含用户侧偏差，一律不扣。不能靠逐点查 existing 判——空隙的边界点坐标恰好等于相邻
            // 已有段的端点，会被误判成"原本有值"而扣掉一个非零偏差，接缝处就冒出一个极窄的尖峰。
            var existing = subtractVibrato ? part.GetEffectivePiecewiseAutomation(key) : null;
            var lines = CollectLines(part, segments, startTick, endTick, !subtractVibrato ? null : ticks =>
            {
                var deviation = part.GetVibratoDeviation(ticks, key);
                var current = existing?.GetValues(ticks);
                for (int i = 0; i < deviation.Length; i++)
                {
                    if (current == null || double.IsNaN(current[i]))
                        deviation[i] = 0;
                }
                return deviation;
            });
            if (lines.Count == 0)
                return;

            if (part.AddEffectivePiecewiseAutomation(key) is { } target)
            {
                foreach (var line in lines)
                    target.AddLine(line, extension);
            }
        }
        else if (part.IsEffectiveAutomation(key))
        {
            // 连续轨：AddLine 入参即真实值域（内部按 DefaultValue 折成锚点偏移），故直传真实值；
            // 但**须先扣掉该轨的 vibrato 偏移**（见 SubtractDeviation）。
            var lines = CollectLines(part, segments, startTick, endTick, ticks => part.GetVibratoDeviation(ticks, key));
            if (lines.Count == 0)
                return;

            if (part.AddEffectiveAutomation(key) is { } target)
            {
                foreach (var line in lines)
                    target.AddLine(line, extension);
            }
        }
    }

    // 关联颤音时的**空隙固定**：把区间内该轨**没有值的那些子区间**固定成模型输出，已画过的段一概不动。
    // 用途：颤音只作用于有值段（NaN 段无基线可叠），故用户把颤音关联到一条有配对回显的分段轨时，
    // 先把覆盖区的空隙填成模型输出，颤音才有东西可叠——"关联颤音"这一个动作里包含"固定基线"，
    // 与被否掉的"持续自动同步"不同：它只在用户主动关联的那一刻发生一次。
    //
    // **绝不覆盖用户已画的值**是硬边界：否则一关联颤音就把手画曲线抹成模型输出。
    // 仅分段轨适用（连续轨处处有值、颤音本就生效，无需也不该固定）。调用方负责事务（含在关联动作的括号内）。
    public static void LockReadbackGaps(this IMidiPart part, AutomationKey key, double startTick, double endTick)
    {
        if (!part.IsEffectivePiecewiseAutomation(key) || !part.HasPairedReadback(key))
            return;

        var segments = GetReadbackSegments(part, key);
        if (segments == null || segments.Count == 0)
            return;

        double partPos = part.Pos.Value;
        double localStart = startTick - partPos;
        double localEnd = endTick - partPos;
        if (localEnd <= localStart)
            return;

        // 已有锚点组按序扫出空隙（part-local tick）；无数据对象 ⇒ 整个区间都是空隙。
        var gaps = new List<(double Start, double End)>();
        double cursor = localStart;
        var existing = part.GetEffectivePiecewiseAutomation(key);
        if (existing != null)
        {
            foreach (var group in existing.AnchorGroups)
            {
                if (group.End <= cursor)
                    continue;

                if (group.Start >= localEnd)
                    break;

                if (group.Start > cursor)
                    gaps.Add((cursor, group.Start));

                cursor = Math.Max(cursor, group.End);
            }
        }
        if (cursor < localEnd)
            gaps.Add((cursor, localEnd));

        // 整段不扣 vibrato：空隙在固定前是 NaN，引擎在那儿用的是自己的模型值、回显不含用户侧偏差。
        foreach (var gap in gaps)
            part.WriteReadbackLock(key, segments, gap.Start + partPos, gap.End + partPos, subtractVibrato: false);
    }

    // 颤音属性编辑的统一前置：对该颤音**已关联的每条**轨做空隙固定（voice 轨 + 各 effect 轨）。
    // 颤音只作用于有值段，所以不论调的是幅度、频率、相位、包络还是位置/时长，编辑后颤音要生效就得有基线。
    // 时机：区间不变的属性（幅度/频率/相位/attack/release）在**落笔**时调；改变区间的（移动/缩放/新建）
    // 在**松手**、新区间定下来之后调——否则填的是旧位置。
    // pitch 不在此列：它走 PitchDeviation 双通道、自由区照样有颤音，无须固定。
    public static void LockAssociatedReadbackGaps(this IMidiPart part, Vibrato vibrato)
    {
        double startTick = vibrato.GlobalStartPos();
        double endTick = vibrato.GlobalEndPos();

        foreach (var kvp in vibrato.AffectedAutomations)
        {
            if (kvp.Value != 0)
                part.LockReadbackGaps(AutomationKey.Voice(kvp.Key), startTick, endTick);
        }

        if (vibrato.AffectedEffectAutomations.Count == 0)
            return;

        for (int i = 0; i < part.Effects.Count; i++)
        {
            string effectId = part.Effects[i].Id;
            foreach (var kvp in vibrato.AffectedEffectAutomations)
            {
                if (kvp.Value != 0 && kvp.Key.EffectId == effectId)
                    part.LockReadbackGaps(AutomationKey.Effect(i, kvp.Key.Id), startTick, endTick);
            }
        }
    }

    // —— 合成音高 → Pitch（专属常驻通道，与参数回显同族）——
    // SynthesizedPitch 与 Pitch 同值域（半音、同一口径，绘制端两者同经 Pitch2Y(value + 0.5)），故直传。

    // 冻结当前合成音高产物引用（同 CaptureReadback 的理由：笔刷第一帧写入即触发失效、引擎可能清掉回显）。
    public static IReadOnlyList<IReadOnlyList<Point>> CaptureSynthesizedPitch(this IMidiPart part)
    {
        return part.SynthesizedPitch;
    }

    // 裸写版（不含事务），供固定笔刷复用；与 WriteReadbackLock 对偶。
    // 合成音高已含 vibrato（引擎收的是 Pitch + PitchDeviation 两条、把偏差加了进去），故写进 Pitch 前须扣掉它。
    public static void WriteSynthesizedPitchLock(this IMidiPart part, IReadOnlyList<IReadOnlyList<Point>> segments, double startTick, double endTick)
    {
        var lines = CollectLines(part, segments, startTick, endTick, part.GetPitchVibratoDeviation);
        if (lines.Count == 0)
            return;

        double extension = Settings.ParameterBoundaryExtension;
        foreach (var line in lines)
            part.Pitch.AddLine(line, extension);
    }

    // —— 内部 ——

    static IReadOnlyOrderedMap<PropertyKey, AutomationConfig>? GetReadbackConfigs(IMidiPart part, AutomationKey key)
    {
        if (key.IsEffect)
            return key.EffectIndex < part.Effects.Count ? part.Effects[key.EffectIndex].SynthesizedParameterConfigs : null;

        return part.SoundSource.SynthesizedParameterConfigs;
    }

    static IReadOnlyList<IReadOnlyList<Point>>? GetReadbackSegments(IMidiPart part, AutomationKey key)
    {
        if (key.IsLane)
            return null;

        IReadOnlyMap<string, SynthesizedParameter> parameters;
        if (key.IsEffect)
        {
            if (key.EffectIndex >= part.Effects.Count)
                return null;

            parameters = part.GetEffectSynthesizedParameters(part.Effects[key.EffectIndex]);
        }
        else
        {
            parameters = part.SynthesizedParameters;
        }

        return parameters.TryGetValue(key.Id, out var parameter) ? parameter.Segments : null;
    }

    // 秒域产物段 → part-local tick 域折线：逐段换算、按区间裁剪（边界处线性插值补点）、扣减 vibrato、按绘制同口径简化。
    // 段间间隙（回显的 NaN 区）不产生线，故分段轨固定后仍在那些位置留空、连续轨则保留原值。
    //
    // deviation ≠ null 时逐点扣掉该轨的 vibrato 偏移：产物是"用户曲线 + 偏差"的结果，原样写回用户曲线会让
    // 重合成再叠一次偏差——每固定一次颤音幅度就翻一圈（同 GetVibratoFallbackPitch 注释里说的二次叠加）。
    // 扣减使固定**幂等**：写入后最终求值恒等于当次产物，反复固定同一段结果不变。
    // 扣减在简化之前：先减成平滑基线再简化，锚点数远少于对含颤音的震荡曲线简化。
    static List<List<Point>> CollectLines(IMidiPart part, IReadOnlyList<IReadOnlyList<Point>> segments, double startTick, double endTick,
                                          Func<IReadOnlyList<double>, double[]>? deviation = null)
    {
        var lines = new List<List<Point>>();
        if (segments.Count == 0)
            return lines;

        var tempoManager = part.TempoManager;
        double partPos = part.Pos.Value;
        double localStart = startTick - partPos;
        double localEnd = endTick - partPos;
        foreach (var segment in segments)
        {
            if (segment.Count == 0)
                continue;

            var points = new List<Point>(segment.Count);
            foreach (var point in segment)
            {
                double tick = tempoManager.GetTick(point.X) - partPos;
                // 同 tick 重复点（回显帧率高于 tick 分辨率处）会让插值退化，取先到的那个。
                if (points.Count > 0 && tick <= points[points.Count - 1].X)
                    continue;

                points.Add(new(tick, point.Y));
            }

            var clipped = Clip(points, localStart, localEnd);
            if (clipped == null)
                continue;

            if (deviation != null)
                SubtractDeviation(clipped, deviation);

            lines.Add(clipped.Count > 2 ? clipped.Simplify(SimplifyGap, SimplifyTolerance) : clipped);
        }
        return lines;
    }

    // 裁剪到 [start, end]：跨边界处按线性插值补一个边界点（保证固定区间内外的接缝落在边界上）。
    // 完全落在区间外返回 null。区间为全轨（±∞）时原样返回。
    static List<Point>? Clip(List<Point> points, double start, double end)
    {
        if (points.Count == 0)
            return null;

        if (double.IsNegativeInfinity(start) && double.IsPositiveInfinity(end))
            return points;

        if (points[points.Count - 1].X <= start || points[0].X >= end)
            return null;

        var result = new List<Point>();
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.X < start)
            {
                if (i + 1 < points.Count && points[i + 1].X > start)
                    result.Add(new(start, ValueAt(point, points[i + 1], start)));

                continue;
            }

            if (point.X > end)
            {
                if (i > 0 && points[i - 1].X < end)
                    result.Add(new(end, ValueAt(points[i - 1], point, end)));

                break;
            }

            result.Add(point);
        }
        return result.Count > 0 ? result : null;
    }

    // 逐点扣掉 vibrato 偏移（查询点 = 该折线的 part-local tick，与偏差求值同域）。
    static void SubtractDeviation(List<Point> points, Func<IReadOnlyList<double>, double[]> deviation)
    {
        var ticks = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
            ticks[i] = points[i].X;

        var deviations = deviation(ticks);
        for (int i = 0; i < points.Count; i++)
            points[i] = new(points[i].X, points[i].Y - deviations[i]);
    }

    static double ValueAt(Point left, Point right, double x)
    {
        double span = right.X - left.X;
        if (span <= 0)
            return left.Y;

        return left.Y + (right.Y - left.Y) * (x - left.X) / span;
    }
}
