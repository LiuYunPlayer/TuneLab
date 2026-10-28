using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI.Components;
using TuneLab.SDK;

using Rect = Avalonia.Rect;
using Size = Avalonia.Size;

namespace TuneLab.UI;

// 参数面板里的插件画布（时间轴画布 TimelineCanvasConfig 与钉进来的侧栏画布 CanvasConfig）：画布 lane 上每个声明了该字段的
// 对象（note 级 = 每个 note、音素级 = 每个显示音素）一块，横向正好占满它的时间段（不留缝）、纵向占满面板。
//
// 分工：每块是一个 PluginCanvas 子控件，负责输入、焦点、弹出与编辑状态；**画由本渲染器在自己的绘制里做**（RenderInto）——
// 画布要按层次夹在别的参数之间：背景参数先画、再整层叠半透明黑底、最后画激活参数，背景画布由此自然被压暗。
// 子控件的位置跟着时间轴（布局晚一拍），而画在绘制时就按当下算出的位置画，故尺寸 / 露出部分由本渲染器直接告诉画布
//（SetHostedGeometry），不等布局。
//
// 实例：按（lane、对象）各一块，各按这个对象自己的声明（part 的按 note 缓存）。看不见不等于失效：滚出视口、切走参数、
// 面板收起时只是不显示、不收事件，回来直接复用；声明换了（part 任何数据变化都会让缓存作废、按新声明重建）才换工厂——
// 插件正在编辑（BeginEdit / 草稿 / 弹出）时不换，等它用完。保留数量不设上限（缩小后它们又都会出来）。
// 背景画布不收输入；插件没处理的滚轮冒泡回本渲染器（滚动 / 缩放时间轴）。
internal partial class AutomationRenderer
{
    readonly record struct CanvasSlot(string LaneId, bool Phoneme, INote Note, int Index);

    sealed class LaneCanvas(PluginCanvas view, CanvasTimeAxis axis)
    {
        public PluginCanvas View { get; } = view;
        public CanvasTimeAxis Axis { get; } = axis;
        public IControllerConfig? Config;   // 当前工厂来自的那份声明：声明对象换了才换工厂
        public Rect Rect;                    // 本渲染器坐标里的位置（这一帧画在哪）
        public bool Shown;
        public bool Active;
    }

    // 时间轴画布的换算：全局秒 ↔ 画布内横坐标，跟着钢琴窗的 tick 轴（速度变化处不均匀）。Left = 画布左缘在本渲染器里的 x。
    sealed class CanvasTimeAxis(AutomationRenderer renderer) : ICanvasTimeAxis
    {
        public double Left;

        public double TimeToX(double time)
            => renderer.Part is { } part ? renderer.TickAxis.Tick2X(part.TempoManager.GetTick(time)) - Left : 0;

        public double XToTime(double x)
            => renderer.Part is { } part ? part.TempoManager.GetTime(renderer.TickAxis.X2Tick(x + Left)) : 0;
    }

    // 画一条画布 lane：视口内声明了该字段的对象各画一块。还没有实例的记下来，画完推迟一拍再建（绘制期间不能动子控件）。
    void DrawCanvasLane(DrawingContext context, AutomationKey key, bool phoneme, bool isActive)
    {
        if (Part is not { } part)
            return;

        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;
        if (!phoneme)
        {
            foreach (var (note, startTick, endTick) in NoteLaneRanges(part))
            {
                if (endTick <= minVisibleTick)
                    continue;
                if (startTick >= maxVisibleTick)
                    break;
                if (part.ResolveNoteLane(note, key.Id) is { } resolved)
                    DrawCanvas(context, new CanvasSlot(key.Id, false, note, -1), resolved, startTick, endTick, isActive);
            }
            return;
        }

        var tempo = part.TempoManager;
        double minVisibleSec = tempo.GetTime(Math.Max(0, minVisibleTick));
        double maxVisibleSec = tempo.GetTime(Math.Max(0, maxVisibleTick));
        foreach (var note in part.Notes)
        {
            if (note.StartTime > maxVisibleSec + LeadExtensionSlack)
                break;
            var phonemes = note.DisplayPhonemes;
            if (phonemes.Count == 0 || phonemes[^1].EndTime <= minVisibleSec)
                continue;
            for (int i = 0; i < phonemes.Count; i++)
            {
                var phonemeItem = phonemes[i];
                if (phonemeItem.EndTime <= minVisibleSec || phonemeItem.StartTime >= maxVisibleSec)
                    continue;
                if (part.ResolvePhonemeLane(note, i, key.Id) is { } resolved)
                    DrawCanvas(context, new CanvasSlot(key.Id, true, note, i), resolved, tempo.GetTick(phonemeItem.StartTime), tempo.GetTick(phonemeItem.EndTime), isActive);
            }
        }
    }

    void DrawCanvas(DrawingContext context, CanvasSlot slot, LanePath.Resolved resolved, double startTick, double endTick, bool isActive)
    {
        var config = resolved.Leaf;
        if (config is not (CanvasConfig or TimelineCanvasConfig))
            return;
        // 不留缝（数值 lane 留 1px 缝是因为段由宿主画）：画布正好覆盖对象的时间段，XToTime(0) / XToTime(Width) 就是它的起止，
        // 跨对象连续的内容也接得上；要不要分隔由插件自己画（底是透明的）。
        double left = TickAxis.Tick2X(startTick);
        double right = TickAxis.Tick2X(endTick);
        if (right - left < 1)
            return;   // 不足 1 像素什么都画不出来；再窄的交给插件按尺寸自己简化

        mCanvasesShown.Add(slot);
        if (!mCanvases.TryGetValue(slot, out var canvas))
        {
            mCanvasesToCreate[slot] = resolved;
            return;
        }

        var rect = new Rect(left, 0, right - left, Bounds.Height);
        canvas.Rect = rect;
        canvas.Active = isActive;
        canvas.Axis.Left = left;
        if (!ReferenceEquals(canvas.Config, config) && !canvas.View.IsInUse)
            ApplyConfig(canvas, slot, resolved);

        var visible = rect.Intersect(new Rect(Bounds.Size));
        canvas.View.SetHostedGeometry(rect.Size, visible.Width > 0 && visible.Height > 0 ? visible.Translate(new(-left, 0)) : default);
        using (context.PushClip(visible))
        using (context.PushTransform(Avalonia.Matrix.CreateTranslation(left, 0)))
            canvas.View.RenderInto(context);
    }

    // 换上这份声明的工厂（连同字段绑定：路径与默认值随声明）。
    void ApplyConfig(LaneCanvas canvas, CanvasSlot slot, LanePath.Resolved resolved)
    {
        var config = resolved.Leaf;
        canvas.Config = config;
        var defaultValue = config is TimelineCanvasConfig timeline ? timeline.DefaultValue : ((CanvasConfig)config).DefaultValue;
        IDataProperty<PropertyValue> field = new LanePathField(slot.Note, slot.Phoneme ? slot.Index : -1, resolved.Steps, defaultValue);
        canvas.View.Bind(field);
        if (config is TimelineCanvasConfig t)
            canvas.View.SetTimelineFactory(t.CreateControl, canvas.Axis);
        else
            canvas.View.SetFactory(((CanvasConfig)config).CreateControl);
    }

    // 一帧画完后（推迟一拍，绘制期间不能动子控件）：建这一帧缺的实例、按这一帧是否画到更新显隐与可否命中、清掉不再存在的，
    // 再按新位置重新布局（命中区域跟上画面）。
    void ScheduleCanvasSync()
    {
        if (mCanvasSyncPosted || (mCanvases.Count == 0 && mCanvasesToCreate.Count == 0))
            return;
        mCanvasSyncPosted = true;
        Dispatcher.UIThread.Post(SyncCanvases);
    }

    void SyncCanvases()
    {
        mCanvasSyncPosted = false;
        var part = Part;
        bool created = false;
        foreach (var (slot, _) in mCanvasesToCreate)
        {
            if (part == null || mCanvases.ContainsKey(slot))
                continue;
            var view = new PluginCanvas() { ExternallyRendered = true };
            view.RenderRequested += OnCanvasRenderRequested;
            var canvas = new LaneCanvas(view, new CanvasTimeAxis(this));
            mCanvases.Add(slot, canvas);
            Children.Add(view);
            created = true;
        }
        mCanvasesToCreate.Clear();

        List<CanvasSlot>? stale = null;
        var notes = part == null ? null : new HashSet<INote>(part.Notes);
        foreach (var (slot, canvas) in mCanvases)
        {
            // 对象没了（note 删了）、lane 不在了（解钉）：不会再出现，丢掉。
            var lanes = slot.Phoneme ? part?.PhonemeLaneConfigs : part?.NoteLaneConfigs;
            if (notes == null || !notes.Contains(slot.Note) || lanes == null || !lanes.ContainsKey(slot.LaneId))
            {
                (stale ??= []).Add(slot);
                continue;
            }
            bool shown = mCanvasesShownLastFrame.Contains(slot);
            canvas.Shown = shown;
            canvas.View.IsVisible = shown;
            canvas.View.IsHitTestVisible = shown && canvas.Active;   // 背景画布不收输入
        }
        if (stale != null)
        {
            foreach (var slot in stale)
                RemoveCanvas(slot);
        }

        InvalidateArrange();
        if (created)
            InvalidateVisual();   // 新建的这一帧还没画：再画一帧
    }

    void RemoveCanvas(CanvasSlot slot)
    {
        if (!mCanvases.Remove(slot, out var canvas))
            return;
        canvas.View.RenderRequested -= OnCanvasRenderRequested;
        canvas.View.Unbind();
        Children.Remove(canvas.View);
    }

    void ClearCanvases()
    {
        foreach (var slot in new List<CanvasSlot>(mCanvases.Keys))
            RemoveCanvas(slot);
        mCanvasesToCreate.Clear();
        mCanvasesShown.Clear();
        mCanvasesShownLastFrame.Clear();
    }

    // 画布请求重画（插件 Invalidate、写草稿、换了工厂……）= 重画本渲染器。正在画的这一帧里来的不算（绘制期间不能再标脏）：
    // 这一帧本就在画，换工厂后新实例当场就画出来了。
    void OnCanvasRenderRequested()
    {
        if (!mInRender)
            InvalidateVisual();
    }

    // 一帧开始与结束：这一帧画到了哪些画布，画完交给推迟的同步（按它定显隐）。
    void BeginCanvasFrame()
    {
        mInRender = true;
        mCanvasesShown.Clear();
    }

    void EndCanvasFrame()
    {
        mInRender = false;
        mCanvasesShownLastFrame.Clear();
        mCanvasesShownLastFrame.UnionWith(mCanvasesShown);
        ScheduleCanvasSync();
    }

    void ArrangeCanvases()
    {
        foreach (var canvas in mCanvases.Values)
            canvas.View.Arrange(canvas.Shown ? canvas.Rect : default);
    }

    readonly Dictionary<CanvasSlot, LaneCanvas> mCanvases = new();
    readonly Dictionary<CanvasSlot, LanePath.Resolved> mCanvasesToCreate = new();
    readonly HashSet<CanvasSlot> mCanvasesShown = new();            // 正在画的这一帧画到的
    readonly HashSet<CanvasSlot> mCanvasesShownLastFrame = new();   // 画完的上一帧画到的（同步按它定显隐）
    bool mCanvasSyncPosted;
    bool mInRender;

    // 画布字段：沿路径（见 LanePath）读写 note 的属性（phonemeIndex >= 0 时是该位音素的属性）。
    // 读 = 路径上存的值，缺的节点读默认值（合成音素没有属性，也读默认值）；写 = 音素级先把这个 note 的音素钉死（合成 → 固定，
    // 保几何，同属性 lane 的写回范式），再沿路径写——没存过的数组 / 列表元素按元素默认值补齐。撤销根、提交、通知都借 note 的
    //（钉死与写值同在一个撤销步）。
    sealed class LanePathField(INote note, int phonemeIndex, IReadOnlyList<LanePath.Step> steps, PropertyValue defaultValue) : IDataProperty<PropertyValue>, IRawValueProperty
    {
        public PropertyValue RawValue => ReadRoot() is { } root ? LanePath.Read(root, steps, defaultValue) : defaultValue;
        public PropertyValue Value => RawValue;
        public PropertyValue GetInfo() => RawValue;
        public void SetInfo(PropertyValue value) => Set(value);
        public void Set(PropertyValue value)
        {
            if (WriteRoot() is { } root)
                LanePath.Write(root, steps, value);
        }

        IDataPropertyObject? ReadRoot()
        {
            if (phonemeIndex < 0)
                return note.Properties;
            return note.HasPinnedPhonemes && phonemeIndex < note.PhonemeCount && note.Phonemes[phonemeIndex].HasProperties
                ? note.Phonemes[phonemeIndex].Properties : null;
        }

        IDataPropertyObject? WriteRoot()
        {
            if (phonemeIndex < 0)
                return note.Properties;
            note.LockPhonemes();   // 幂等
            return phonemeIndex < note.PhonemeCount ? note.Phonemes[phonemeIndex].Properties : null;
        }

        public IModifiedEvent Modified => note.Modified;
        public IModifiedEvent WillModify => note.WillModify;
        public Head Head => note.Head;
        public void Attach(IDataObject parent) => note.Attach(parent);
        public void Detach() => note.Detach();
        public IDisposable MergeNotify() => note.MergeNotify();
        public void BeginMergeNotify() => note.BeginMergeNotify();
        public void EndMergeNotify() => note.EndMergeNotify();
        public bool Pushable() => note.Pushable();
        public bool Commit() => note.Commit();
        public bool Discard() => note.Discard();
        public bool DiscardTo(Head head) => note.DiscardTo(head);
        public bool Undo() => note.Undo();
        public bool Redo() => note.Redo();
    }
}
