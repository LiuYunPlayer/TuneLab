using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Data.Timing;
using TuneLab.Foundation;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.SDK;
using TuneLab.Utils;

using Rect = Avalonia.Rect;

namespace TuneLab.UI;

// 叠到钢琴窗上的画布（PianoOverlay，存储见 TuneLab.Configs.PianoOverlay）：铺在音符区上，和当前工具一起操作（不分工具）。
// 时间轴画布一块铺满音符区；画布 lane 每个音符（音素级的每个音素）一块，横向 = 它的时间段（同参数区的口径，NoteLaneRanges），
// 纵向铺满音符区。纵轴 = 钢琴窗的音高轴（n 号键占 [n, n + 1)，同参数区）。
//
// 一摞层（见 docs/canvas-stacking.md）：叠上去的画布按用户排的顺序自下而上，激活的临时在最上，最底下是当前工具（本视图自己的鼠标处理）。
// 事件由 Container 按叠放顺序逐层问，画布不接的落到工具上。宿主不画分界——音符本身看得见。
//
// 画由本视图在自己的绘制里做（RenderInto，画在音符与音高线之上、框选框与波形之下）：插件画布自己画时带一块输入框底色，
// 叠在音符上要透明。实例、推迟一拍建、按这一帧画到的定显隐与命中顺序，都同参数区（AutomationRendererCanvases）。
internal partial class PianoScrollView
{
    readonly record struct OverlaySlot(AutomationKey Key, INote? Note, int Index);

    sealed class OverlayCanvas(PluginCanvas view, OverlayTimeAxis axis, OverlayPitchAxis pitchAxis)
    {
        public PluginCanvas View { get; } = view;
        public OverlayTimeAxis Axis { get; } = axis;
        public OverlayPitchAxis PitchAxis { get; } = pitchAxis;
        public object? Config;   // 当前工厂来自的那份声明：声明对象换了才换工厂
        public Rect Rect;
        public bool Shown;
        public int Order;
    }

    // 画布的时间换算：同参数区的画布 lane / 时间轴画布。Left = 画布左缘在本视图里的 x；Origin = 局部 tick 的原点（工程 tick）。
    sealed class OverlayTimeAxis(PianoScrollView view) : ICanvasTimeAxis
    {
        public double Left;
        public double Origin;

        public double SecondToX(double second)
            => view.Part is { } part ? view.TickAxis.Tick2X(part.TempoManager.GetTick(second)) - Left : 0;

        public double XToSecond(double x)
            => view.Part is { } part ? part.TempoManager.GetTime(view.TickAxis.X2Tick(x + Left)) : 0;

        public double TickToX(double tick) => view.TickAxis.Tick2X(Origin + tick) - Left;

        public double XToTick(double x) => view.TickAxis.X2Tick(x + Left) - Origin;

        public ITimeConverter TimeConverter
            => new LocalTimeConverter(view.Part?.TempoManager.CreateSnapshot() ?? OverlayEmptyTempo, Origin);
    }

    static readonly TempoSnapshot OverlayEmptyTempo = new([new TempoMark(0, 120)], MusicTheory.RESOLUTION);

    // 画布的音高换算：画布上缘贴着音符区上缘，纵轴就是钢琴窗的音高轴。
    sealed class OverlayPitchAxis(PianoScrollView view) : ICanvasPitchAxis
    {
        public double PitchToY(double pitch) => view.PitchAxis.Pitch2Y(pitch);

        public double YToPitch(double y) => view.PitchAxis.Y2Pitch(y);
    }

    // 音符区的下沿：波形带显示着就到波形带上沿，否则到参数区上沿。画布只铺音符区。
    double OverlayBottom => Math.Max(0, mDependency.IsWaveformVisible ? WaveformTop : WaveformBottom);

    // 画叠着的画布（自下而上，激活的最后画）。没开小眼睛的不画、不收事件。
    void DrawOverlays(DrawingContext context)
    {
        mOverlayInRender = true;
        mOverlaysShown.Clear();
        mOverlayOrder = 0;
        try
        {
            if (Part is not { } part || OverlayBottom < 1)
                return;
            foreach (var layer in PianoOverlay.DrawOrder(part))
            {
                if (!layer.Shown)
                    continue;
                if (layer.Key.IsTimelineCanvas)
                    DrawOverlayTimeline(context, part, layer.Key);
                else
                    DrawOverlayLane(context, part, layer.Key);
            }
        }
        finally
        {
            mOverlayInRender = false;
            mOverlaysShownLastFrame.Clear();
            mOverlaysShownLastFrame.UnionWith(mOverlaysShown);
            ScheduleOverlaySync();
        }
    }

    // 一条画布 lane：视口内声明了这个字段的对象各一块。叠放同参数区：先按音符顺序放没选中的，再放选中的，后放的在上。
    void DrawOverlayLane(DrawingContext context, IMidiPart part, AutomationKey key)
    {
        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;
        if (key.IsNoteLane)
        {
            foreach (bool selectedPass in OverlaySelectionPasses)
            {
                foreach (var (note, startTick, endTick) in AutomationRenderer.NoteLaneRanges(part))
                {
                    if (endTick <= minVisibleTick)
                        continue;
                    if (startTick >= maxVisibleTick)
                        break;
                    if (note.IsSelected == selectedPass && part.ResolveNoteLane(note, key.Id) is { } resolved)
                        DrawOverlayBlock(context, new OverlaySlot(key, note, -1), resolved, startTick, endTick);
                }
            }
            return;
        }

        var tempo = part.TempoManager;
        double minVisibleSec = tempo.GetTime(Math.Max(0, minVisibleTick));
        double maxVisibleSec = tempo.GetTime(Math.Max(0, maxVisibleTick));
        foreach (bool selectedPass in OverlaySelectionPasses)
        foreach (var note in part.Notes)
        {
            if (note.StartTime > maxVisibleSec + OverlayLeadSlack)
                break;
            if (note.IsSelected != selectedPass)
                continue;
            var phonemes = note.DisplayPhonemes;
            if (phonemes.Count == 0 || phonemes[^1].EndTime <= minVisibleSec)
                continue;
            for (int i = 0; i < phonemes.Count; i++)
            {
                var phoneme = phonemes[i];
                if (phoneme.EndTime <= minVisibleSec || phoneme.StartTime >= maxVisibleSec)
                    continue;
                if (part.ResolvePhonemeLane(note, i, key.Id) is { } resolved)
                    DrawOverlayBlock(context, new OverlaySlot(key, note, i), resolved, tempo.GetTick(phoneme.StartTime), tempo.GetTick(phoneme.EndTime));
            }
        }
    }

    // 音素可以在音符开始之前（辅音前置）：按音符起点提前跳出时留的余量（秒），同参数区。
    const double OverlayLeadSlack = 2;
    static readonly bool[] OverlaySelectionPasses = [false, true];   // 先没选中的、后选中的

    void DrawOverlayBlock(DrawingContext context, OverlaySlot slot, LanePath.Resolved resolved, double startTick, double endTick)
    {
        if (resolved.Leaf is not CanvasLaneConfig config)
            return;
        double left = TickAxis.Tick2X(startTick);
        double right = TickAxis.Tick2X(endTick);
        if (right - left < 1)
            return;

        mOverlaysShown.Add(slot);
        if (!mOverlays.TryGetValue(slot, out var canvas))
        {
            mOverlaysToCreate.Add(slot);
            return;
        }

        var rect = new Rect(left, 0, right - left, OverlayBottom);
        canvas.Rect = rect;
        canvas.Axis.Left = left;
        canvas.Axis.Origin = slot.Note!.GlobalStartPos();   // 局部 tick 的原点：所属音符的起点（音素级同样）
        canvas.Order = mOverlayOrder++;
        if (!ReferenceEquals(canvas.Config, config) && !canvas.View.IsInUse)
        {
            canvas.Config = config;
            canvas.View.Bind(new AutomationRenderer.LanePathField(slot.Note, slot.Index, resolved.Steps));
            canvas.View.SetLaneFactory(config.CreateControl, canvas.Axis, canvas.PitchAxis);
        }
        RenderOverlay(context, canvas, rect);
    }

    void DrawOverlayTimeline(DrawingContext context, IMidiPart part, AutomationKey key)
    {
        if (!part.TryGetTimelineCanvas(key, out var config, out var store) || config.CreateControl == null)
            return;

        var slot = new OverlaySlot(key, null, -1);
        mOverlaysShown.Add(slot);
        if (!mOverlays.TryGetValue(slot, out var canvas))
        {
            mOverlaysToCreate.Add(slot);
            return;
        }

        var rect = new Rect(0, 0, Bounds.Width, OverlayBottom);
        canvas.Rect = rect;
        canvas.Axis.Left = 0;
        canvas.Axis.Origin = part.Pos.Value + (store.TryGetValue(key.Id, out var entry) ? entry.Offset : 0);
        canvas.Order = mOverlayOrder++;
        if (!ReferenceEquals(canvas.Config, config) && !canvas.View.IsInUse)
        {
            canvas.Config = config;
            canvas.View.Bind(new AutomationRenderer.TimelineCanvasField(store, key.Id));
            canvas.View.SetTimelineFactory(config.CreateControl, canvas.Axis, canvas.PitchAxis);
        }
        RenderOverlay(context, canvas, rect);
    }

    void RenderOverlay(DrawingContext context, OverlayCanvas canvas, Rect rect)
    {
        var visible = rect.Intersect(new Rect(0, 0, Bounds.Width, OverlayBottom));
        canvas.View.SetHostedGeometry(rect.Size, visible.Width > 0 && visible.Height > 0 ? visible.Translate(new(-rect.X, -rect.Y)) : default);
        using (context.PushClip(visible))
        using (context.PushTransform(Avalonia.Matrix.CreateTranslation(rect.X, rect.Y)))
            canvas.View.RenderInto(context);
    }

    // 一帧画完后（推迟一拍，绘制期间不能动子控件）：建缺的实例、按这一帧画到没有定显隐与可否命中、同步命中顺序、
    // 丢掉不会再出现的（音符删了、这块画布不再被声明），再重新布局。取下的、关了小眼睛的只是不显示，回来直接复用。
    void ScheduleOverlaySync()
    {
        if (mOverlaySyncPosted || (mOverlays.Count == 0 && mOverlaysToCreate.Count == 0))
            return;
        mOverlaySyncPosted = true;
        Dispatcher.UIThread.Post(SyncOverlays);
    }

    void SyncOverlays()
    {
        mOverlaySyncPosted = false;
        var part = Part;
        bool created = false;
        foreach (var slot in mOverlaysToCreate)
        {
            if (part == null || mOverlays.ContainsKey(slot))
                continue;
            var view = new PluginCanvas() { ExternallyRendered = true };
            view.RenderRequested += OnOverlayRenderRequested;
            // 右键落在范围选区带内 → 宿主先截，弹选区菜单（同参数区）；带外的右键照常先问插件。
            view.HostTakesPress = e => e.MouseButtonType == MouseButtonType.SecondaryButton && IsInRegion(view.Bounds.X + e.Position.X);
            mOverlays.Add(slot, new OverlayCanvas(view, new OverlayTimeAxis(this), new OverlayPitchAxis(this)));
            Children.Add(view);
            created = true;
        }
        mOverlaysToCreate.Clear();

        List<OverlaySlot>? stale = null;
        var notes = part == null ? null : new HashSet<INote>(part.Notes);
        foreach (var (slot, canvas) in mOverlays)
        {
            bool gone = slot.Key.IsTimelineCanvas
                ? part == null || !part.IsEffectiveTimelineCanvas(slot.Key)
                : notes == null || !notes.Contains(slot.Note!)
                    || !(slot.Key.IsNoteLane ? part!.NoteLaneConfigs : part!.PhonemeLaneConfigs).ContainsKey(slot.Key.Id);
            if (gone && !canvas.View.IsInUse)
            {
                (stale ??= []).Add(slot);
                continue;
            }
            bool shown = mOverlaysShownLastFrame.Contains(slot);
            canvas.Shown = shown;
            canvas.View.IsVisible = shown;
            canvas.View.IsHitTestVisible = shown;
            // 叠在歌词 / 音素输入框（ZIndex 0）之下：输入框弹出时总在最上。
            int zIndex = canvas.Order - OverlayZIndexBase;
            if (canvas.View.ZIndex != zIndex)
                canvas.View.ZIndex = zIndex;
        }
        if (stale != null)
        {
            foreach (var slot in stale)
                RemoveOverlay(slot);
        }

        InvalidateArrange();
        if (created)
            InvalidateVisual();   // 新建的这一帧还没画：再画一帧
    }

    const int OverlayZIndexBase = 1_000_000;

    void RemoveOverlay(OverlaySlot slot)
    {
        if (!mOverlays.Remove(slot, out var canvas))
            return;
        canvas.View.RenderRequested -= OnOverlayRenderRequested;
        canvas.View.Unbind();
        Children.Remove(canvas.View);
    }

    // 换 part：上一个 part 的实例全部丢掉。
    void ClearOverlays()
    {
        foreach (var slot in new List<OverlaySlot>(mOverlays.Keys))
            RemoveOverlay(slot);
        mOverlaysToCreate.Clear();
        mOverlaysShown.Clear();
        mOverlaysShownLastFrame.Clear();
    }

    void OnOverlayRenderRequested()
    {
        if (!mOverlayInRender)
            InvalidateVisual();
    }

    void ArrangeOverlays()
    {
        foreach (var canvas in mOverlays.Values)
            canvas.View.Arrange(canvas.Shown ? canvas.Rect : default);
    }

    readonly Dictionary<OverlaySlot, OverlayCanvas> mOverlays = new();
    readonly HashSet<OverlaySlot> mOverlaysToCreate = new();
    readonly HashSet<OverlaySlot> mOverlaysShown = new();
    readonly HashSet<OverlaySlot> mOverlaysShownLastFrame = new();
    int mOverlayOrder;
    bool mOverlaySyncPosted;
    bool mOverlayInRender;
}
