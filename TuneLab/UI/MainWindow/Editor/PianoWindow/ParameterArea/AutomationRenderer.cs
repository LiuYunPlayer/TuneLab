using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Animation;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.SDK;
using Avalonia.Media;
using TuneLab.GUI.Input;
using TuneLab.GUI.Components;
using Avalonia;
using TuneLab.Foundation;
using TuneLab.Utils;
using Avalonia.Controls;
using TuneLab.I18N;
using Avalonia.Threading;

using Point = Avalonia.Point;

namespace TuneLab.UI;

internal partial class AutomationRenderer : View
{
    public TickAxis TickAxis => mDependency.TickAxis;
    public IMidiPart? Part => mDependency.PartHolder.Value;
    public bool IsOperating => mState != State.None;

    public interface IDependency
    {
        event Action? ActiveAutomationChanged;
        event Action? VisibleAutomationChanged;
        // 合成参数回显轨显隐变化（只读轨集合，独立于可编辑轨的 Visible/Active 机制）。
        event Action? ReadbackVisibilityChanged;
        IHolder<IMidiPart> PartHolder { get; }
        PianoScrollView PianoScrollView { get; }
        TickAxis TickAxis { get; }
        AutomationKey? ActiveAutomation { get; }
        bool IsAutomationVisible(AutomationKey automation);
        IReadOnlyList<AutomationKey> VisibleAutomations { get; }
        // 回显轨声明（按 AutomationKey 分源：voice / 各 effect，只读）：曲线数据经 Part 按同一批 key 承载。
        IReadOnlyOrderedMap<AutomationKey, AutomationConfigEntry> ReadbackConfigs { get; }
        bool IsReadbackVisible(AutomationKey key);
        INotifiableProperty<PianoTool> PianoTool { get; }
    }

    public AutomationRenderer(IDependency dependency)
    {
        mDependency = dependency;

        mMiddleDragOperation = new(this);
        mRegionSelectionOperation = new(this);
        mDrawOperation = new(this);
        mClearOperation = new(this);
        mAnchorSelectOperation = new(this);
        mAnchorDeleteOperation = new(this);
        mAnchorMoveOperation = new(this);
        mVibratoAmplitudeOperation = new(this);

        mPiecewiseDrawOperation = new(this);
        mPiecewiseClearOperation = new(this);
        mNoteLaneDrawOperation = new(this);
        mNoteLaneClearOperation = new(this);
        mPhonemeLaneDrawOperation = new(this);
        mPhonemeLaneClearOperation = new(this);
        mPiecewiseAnchorDeleteOperation = new(this);
        mPiecewiseAnchorMoveOperation = new(this);
        mPiecewiseAnchorSelectOperation = new(this);

        mAnchorValueInput = new()
        {
            IsVisible = false,
            Width = AnchorValueInputWidth,
            Height = AnchorValueInputHeight,
            Padding = new(0),
            FontFamily = Assets.SegoeUI,
            FontSize = 12,
            CornerRadius = new(4),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = Style.BACK.ToBrush(),
            Foreground = Style.LIGHT_WHITE.ToBrush(),
        };
        mAnchorValueInput.EndInput.Subscribe(OnAnchorValueInputEndInput, s);
        mAnchorValueInput.GotFocus += OnAnchorValueInputGotFocus;
        mAnchorValueInput.KeyDown += OnAnchorValueInputKeyDown;
        mAnchorValueInput.PointerPressed += OnAnchorValueInputPointerPressed;
        Children.Add(mAnchorValueInput);

        mDependency.PartHolder.Modified.Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.When(p => p.Automations.Modified).Subscribe(InvalidateVisual, s);
        // 分段轨数据（voice 级在 part.PiecewiseAutomations、effect 级在各 effect.PiecewiseAutomations）编辑后重绘。
        mDependency.PartHolder.When(p => p.PiecewiseAutomations.Modified).Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.When(p => p.Effects.WhenAny(effect => effect.PiecewiseAutomations.Modified)).Subscribe(InvalidateVisual, s);
        // effect 自动化数据在各 effect.Automations 里，编辑它不会触发 part.Automations.Modified，需单独订阅（否则拖动不重绘）。
        mDependency.PartHolder.When(p => p.Effects.WhenAny(effect => effect.Automations.Modified)).Subscribe(InvalidateVisual, s);
        // 合成状态/产物更新（插件在合成过程中逐步填入回显曲线，经 StatusChanged 通知）→ 重绘，
        // 否则参数区的合成参数回显要等下次鼠标事件等其它失效源才刷新（滞后）。
        mDependency.PartHolder.When(p => p.SynthesisStatusChanged).Subscribe(InvalidateVisual, s);
        // 属性 lane（钉选 note/phoneme 属性）随 note/音素增删/时值/属性值变重绘；无 lane 时不扰动（note 拖拽是高频路径）。
        mDependency.PartHolder.When(p => p.Notes.Modified).Subscribe(() =>
        {
            if (Part != null && (Part.NoteLaneConfigs.Count > 0 || Part.PhonemeLaneConfigs.Count > 0))
                InvalidateVisual();
        }, s);
        // phoneme lane 的段几何来自显示音素（合成回填驱动）：回填新音素后段的位置/数量会变，须重绘。
        mDependency.PartHolder.When(p => p.SynthesizedPhonemesChanged).Subscribe(() =>
        {
            if (Part != null && Part.PhonemeLaneConfigs.Count > 0)
                InvalidateVisual();
        }, s);
        mDependency.PartHolder.When(p => p.Vibratos.Modified).Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.When(p => p.Pos.Modified).Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.Modified.Subscribe(UpdateAnchorValueInput, s);
        mDependency.PartHolder.When(p => p.Automations.Modified).Subscribe(UpdateAnchorValueInput, s);
        mDependency.PartHolder.When(p => p.Effects.WhenAny(effect => effect.Automations.Modified)).Subscribe(UpdateAnchorValueInput, s);
        mDependency.PartHolder.When(p => p.Pos.Modified).Subscribe(UpdateAnchorValueInput, s);
        // 条件自动化轨集合随参数 commit 变（轨随值显隐）→ 重绘曲线 + 刷新锚点输入框；
        // 否则轨集合变了但本视图无失效源，要等下次鼠标事件才触发重绘（曲线滞留/慢一拍）。
        mDependency.PartHolder.When(p => p.AutomationConfigsModified).Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.When(p => p.AutomationConfigsModified).Subscribe(UpdateAnchorValueInput, s);
        mDependency.PianoTool.Modified.Subscribe(Update, s);
        mDependency.PianoTool.Modified.Subscribe(UpdateAnchorValueInput, s);
        mDependency.ActiveAutomationChanged += InvalidateVisual;
        mDependency.ActiveAutomationChanged += UpdateAnchorValueInput;
        mDependency.VisibleAutomationChanged += InvalidateVisual;
        mDependency.ReadbackVisibilityChanged += InvalidateVisual;
        mDependency.TickAxis.AxisChanged += Update;
        mDependency.TickAxis.AxisChanged += UpdateAnchorValueInput;
        mDependency.PianoScrollView.RegionSelectionChanged += InvalidateVisual;   // 范围选区变化（任一区拖/清）→ 参数区重绘同一条 tick 带
    }

    ~AutomationRenderer()
    {
        s.DisposeAll();
        mDependency.PianoScrollView.RegionSelectionChanged -= InvalidateVisual;
        mDependency.ActiveAutomationChanged -= InvalidateVisual;
        mDependency.ActiveAutomationChanged -= UpdateAnchorValueInput;
        mDependency.VisibleAutomationChanged -= InvalidateVisual;
        mDependency.ReadbackVisibilityChanged -= InvalidateVisual;
        mDependency.TickAxis.AxisChanged -= Update;
        mDependency.TickAxis.AxisChanged -= UpdateAnchorValueInput;
        mAnchorValueInput.GotFocus -= OnAnchorValueInputGotFocus;
        mAnchorValueInput.KeyDown -= OnAnchorValueInputKeyDown;
        mAnchorValueInput.PointerPressed -= OnAnchorValueInputPointerPressed;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateAnchorValueInput(false);
        if (mAnchorValueInput.IsVisible && TryGetAnchorValueInputRect(out var rect))
            mAnchorValueInput.Arrange(rect);
        else
            mAnchorValueInput.Arrange(new Rect());

        return finalSize;
    }

    protected override void OnRender(DrawingContext context)
    {
        context.FillRectangle(Colors.Black.Opacity(0.25).ToBrush(), this.Rect());
        if (Part == null)
            return;

        double step = 1;
        int n = (int)Math.Ceiling(Bounds.Width / step);
        if (n <= 0)
            return;

        double[] xs = new double[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = i * step;
        }

        double pos = Part.Pos.Value;
        var ticks = new double[n];
        for (int i = 0; i < n; i++)
        {
            ticks[i] = TickAxis.X2Tick(xs[i]) - pos;
        }

        double lineWidth = 1;

        // 按 kind 分派：连续轨画含 vibrato 的最终值（处处有值）；分段轨画分段曲线（段间 NaN，断开不连线）；
        // 属性 lane 画逐 note / 逐音素值段（顶线 + active 时半透明柱）。
        void Draw(AutomationKey automationID, bool isActive)
        {
            if (Part.IsEffectiveNoteLane(automationID))
                DrawNoteLane(automationID, isActive);
            else if (Part.IsEffectivePhonemeLane(automationID))
                DrawPhonemeLane(automationID, isActive);
            else if (Part.IsEffectiveAutomation(automationID))
                DrawContinuous(automationID);
            else if (Part.IsEffectivePiecewiseAutomation(automationID))
                DrawPiecewise(automationID);
        }

        // note lane：每个 note 一段——顶部实色横线是值指示（与分段轨的段视觉同族），active 时其下再垫半透明柱，
        // 给块感与 note 对应关系。段区间经 NoteLaneRanges 去重叠；右端再内缩 1px 留缝，相邻 note 不连成墙；
        // 未写过值的 note 画在 DefaultValue 高度。
        void DrawNoteLane(AutomationKey automationID, bool isActive)
        {
            var entry = Part.GetNoteLaneEntry(automationID);
            var color = Color.Parse(entry.Color);
            var lineBrush = color.ToBrush();
            var fillBrush = color.Opacity(0.25).ToBrush();
            double minVisibleTick = TickAxis.MinVisibleTick;
            double maxVisibleTick = TickAxis.MaxVisibleTick;
            double topLineWidth = isActive ? 2 : 1;
            var laneDefault = PropertyValue.Create(entry.DefaultValue);
            foreach (var (note, startTick, endTick) in NoteLaneRanges(Part))
            {
                if (endTick <= minVisibleTick)
                    continue;

                if (startTick >= maxVisibleTick)
                    break;

                double left = TickAxis.Tick2X(startTick);
                double right = TickAxis.Tick2X(endTick) - 1;   // 1px 缝
                if (right <= left)
                    right = left + 1;

                note.Properties.GetValue(automationID.Id, laneDefault).ToDouble(out double value);
                double y = ValueToY(value, entry.MinValue, entry.MaxValue);
                if (isActive)
                    context.FillRectangle(fillBrush, new Rect(left, y, right - left, Bounds.Height - y));
                context.FillRectangle(lineBrush, new Rect(left, Math.Max(0, y - topLineWidth / 2), right - left, topLineWidth));
            }
        }

        // phoneme lane：每个显示音素一段，几何走 note.DisplayPhonemes（绝对秒、跨 note 已去重叠——与波形带同口径）。
        // 值只在钉死音素上存在（经 HasProperties 闸门读、不触发 lazy 物化）；合成音素画默认值高度。
        // 邻接未解析的 note 其 DisplayPhonemes 为空 → 暂无段，合成回填后经订阅重绘补上。
        void DrawPhonemeLane(AutomationKey automationID, bool isActive)
        {
            var entry = Part.GetPhonemeLaneEntry(automationID);
            if (!entry.Resolved)
                return;   // 未解析占位（合成音素未产出）：tab 在场但无段可画、量程未知

            var color = Color.Parse(entry.Color);
            var lineBrush = color.ToBrush();
            var fillBrush = color.Opacity(0.25).ToBrush();
            var tempoManager = Part.TempoManager;
            double minVisibleSec = tempoManager.GetTime(Math.Max(0, TickAxis.MinVisibleTick));
            double maxVisibleSec = tempoManager.GetTime(Math.Max(0, TickAxis.MaxVisibleTick));
            double topLineWidth = isActive ? 2 : 1;
            var laneDefault = PropertyValue.Create(entry.DefaultValue);
            foreach (var note in Part.Notes)
            {
                // 音素段可越出 note 几何（前置辅音前伸 / 元音向满末充填），note 级过滤只做右侧宽松截断、逐段精筛。
                if (note.StartTime > maxVisibleSec + LeadExtensionSlack)
                    break;

                var phonemes = note.DisplayPhonemes;
                if (phonemes.Count == 0 || phonemes[^1].EndTime <= minVisibleSec)
                    continue;

                bool pinned = note.Phonemes.Count > 0;
                for (int i = 0; i < phonemes.Count; i++)
                {
                    var phoneme = phonemes[i];
                    if (phoneme.EndTime <= minVisibleSec || phoneme.StartTime >= maxVisibleSec)
                        continue;

                    double left = TickAxis.Tick2X(tempoManager.GetTick(phoneme.StartTime));
                    double right = TickAxis.Tick2X(tempoManager.GetTick(phoneme.EndTime)) - 1;   // 1px 缝
                    if (right <= left)
                        right = left + 1;

                    double value = entry.DefaultValue;
                    if (pinned && i < note.Phonemes.Count && note.Phonemes[i].HasProperties)
                        note.Phonemes[i].Properties.GetValue(automationID.Id, laneDefault).ToDouble(out value);
                    double y = ValueToY(value, entry.MinValue, entry.MaxValue);
                    if (isActive)
                        context.FillRectangle(fillBrush, new Rect(left, y, right - left, Bounds.Height - y));
                    context.FillRectangle(lineBrush, new Rect(left, Math.Max(0, y - topLineWidth / 2), right - left, topLineWidth));
                }
            }
        }

        void DrawContinuous(AutomationKey automationID)
        {
            var config = Part.GetEffectiveAutomationConfig(automationID);
            double min = config.MinValue;
            double max = config.MaxValue;
            double range = max - min;
            double r = Bounds.Height / range;

            double[] values = Part.GetFinalAutomationValues(ticks, automationID);

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (max - values[i].Limit(min, max)) * r;
            }

            var points = new Point[n];
            for (int i = 0; i < n; i++)
            {
                points[i] = new(xs[i], values[i]);
            }

            context.DrawCurve(points, Color.Parse(config.Color), lineWidth);
        }

        void DrawPiecewise(AutomationKey automationID)
        {
            var config = Part.GetEffectivePiecewiseAutomationConfig(automationID);
            double min = config.MinValue;
            double max = config.MaxValue;

            // 已画的可编辑曲线（数据对象按需创建——未编辑过则无数据、只画回显）。段间取值 NaN → 断开。
            var data = Part.GetEffectivePiecewiseAutomation(automationID);
            if (data != null)
            {
                double[] values = data.GetValues(ticks);
                var ys = new double[n];
                for (int i = 0; i < n; i++)
                    ys[i] = double.IsNaN(values[i]) ? double.NaN : ValueToY(values[i], min, max);
                DrawBrokenCurve(ys, Color.Parse(config.Color), lineWidth);
            }
        }

        // 在 NaN 处断开的折线：把连续非 NaN 段各自成段绘制（分段轨段间空、跨段不连线）。
        void DrawBrokenCurve(double[] ys, Color color, double width)
        {
            var run = new List<Point>();
            void Flush()
            {
                if (run.Count >= 2)
                    context.DrawCurve(run.ToArray(), color, width);
                run.Clear();
            }
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(ys[i]))
                    Flush();
                else
                    run.Add(new Point(xs[i], ys[i]));
            }
            Flush();
        }

        var activeAutomation = mDependency.ActiveAutomation;
        foreach (var automation in mDependency.VisibleAutomations)
        {
            if (automation == activeAutomation)
                continue;

            if (!mDependency.IsAutomationVisible(automation))
                continue;

            Draw(automation, false);
        }

        context.FillRectangle(Colors.Black.Opacity(0.25).ToBrush(), this.Rect());

        // 合成参数回显轨（只读）：在可编辑轨之上、活动轨之下绘制为半透明积分面积。可独立显隐（标题栏管控）。
        DrawReadbackParameters(context);

        DrawRegionSelection(context);

        if (activeAutomation == null)
            return;

        var active = activeAutomation.Value;
        bool activeContinuous = Part.IsEffectiveAutomation(active);
        bool activePiecewise = !activeContinuous && Part.IsEffectivePiecewiseAutomation(active);
        bool activeNoteLane = Part.IsEffectiveNoteLane(active);
        bool activePhonemeLane = Part.IsEffectivePhonemeLane(active);
        if (!activeContinuous && !activePiecewise && !activeNoteLane && !activePhonemeLane)
            return;

        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;
        // 量程与显示口径按来源取：automation 走 config、属性 lane 走 entry——两者同带 Min/MaxLabel 描述文本
        //（lane 的 label 来自 SliderConfig 声明，与滑条两端显示同源）。
        double min, max;
        SDK.INumberFormat? numberFormat;
        string? minLabel, maxLabel;
        string colorStr;
        bool boundsKnown = true;
        if (activeNoteLane || activePhonemeLane)
        {
            var entry = activeNoteLane ? Part.GetNoteLaneEntry(active) : Part.GetPhonemeLaneEntry(active);
            min = entry.MinValue;
            max = entry.MaxValue;
            numberFormat = entry.Format;
            minLabel = entry.MinLabel;
            maxLabel = entry.MaxLabel;
            colorStr = entry.Color;
            boundsKnown = entry.Resolved;   // 未解析占位：量程未知，不显上下界
        }
        else
        {
            var config = activeContinuous
                ? Part.GetEffectiveAutomationConfig(active)
                : Part.GetEffectivePiecewiseAutomationConfig(active);
            min = config.MinValue;
            max = config.MaxValue;
            numberFormat = config.Format;
            minLabel = config.MinLabel;
            maxLabel = config.MaxLabel;
            colorStr = config.Color;
        }

        // vibrato 叠加层与"拖拽关联颤音"提示仅对 voice 连续轨绘制（effect 与分段轨皆无 automation-vibrato 概念）。
        if (activeContinuous && !active.IsEffect)
        {
            foreach (var vibrato in Part.Vibratos)
            {
                if (vibrato.GlobalEndPos() <= minVisibleTick)
                    continue;

                if (vibrato.GlobalStartPos() >= maxVisibleTick)
                    break;

                double startX = TickAxis.Tick2X(vibrato.GlobalStartPos());
                double endX = TickAxis.Tick2X(vibrato.GlobalEndPos());
                double[] vxs = new double[(int)(endX - startX) + 1];
                for (int i = 0; i < vxs.Length; i++)
                {
                    vxs[i] = startX + i;
                }

                var vticks = new double[vxs.Length];
                for (int i = 0; i < vxs.Length; i++)
                {
                    vticks[i] = TickAxis.X2Tick(vxs[i]) - pos;
                }

                double range = max - min;
                double r = Bounds.Height / range;

                double[] values = Part.GetAutomationValues(vticks, active.Id);

                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = (max - values[i].Limit(min, max)) * r;
                }

                var points = new Point[vxs.Length];
                for (int i = 0; i < vxs.Length; i++)
                {
                    points[i] = new(vxs[i], values[i]);
                }

                context.DrawCurve(points, Color.Parse(colorStr).Opacity(0.5), lineWidth);
            }

            if (IsHover && ItemAt(MousePosition) is VibratoItem vibratoItem)
            {
                if (!vibratoItem.Vibrato.AffectedAutomations.ContainsKey(active.Id))
                {
                    var vibrato = vibratoItem.Vibrato;
                    double x = TickAxis.Tick2X(vibrato.GlobalStartPos() + vibrato.Dur / 2);
                    context.DrawString("Drag to associate the vibrato".Tr(this), new Point(x, 8), Brushes.White, 12, Alignment.CenterTop);
                }
            }
        }

        lineWidth = 2;
        Draw(active, true);

        if (mAnchorSelectOperation.IsOperating || mPiecewiseAnchorSelectOperation.IsOperating)
        {
            var rect = mAnchorSelectOperation.IsOperating ? mAnchorSelectOperation.SelectionRect() : mPiecewiseAnchorSelectOperation.SelectionRect();
            var selectionColor = GUI.Style.HIGH_LIGHT;
            context.DrawRectangle(selectionColor.Opacity(0.25).ToBrush(), new Pen(selectionColor.ToUInt32()), rect);
        }

        // 上下界处显示：优先用描述文本（MaxLabel/MinLabel，插件自译），否则按 Format 格式化数值（缺省两位小数带符号）。
        if (boundsKnown)
        {
            string BoundText(double value) => numberFormat is { } f ? f.Format(value) : value.ToString("+0.00;-0.00");
            context.DrawString(maxLabel ?? BoundText(max), new Point(8, 12), Style.LIGHT_WHITE.ToBrush(), 12, Alignment.LeftCenter);
            context.DrawString(minLabel ?? BoundText(min), new Point(8, Bounds.Height - 12), Style.LIGHT_WHITE.ToBrush(), 12, Alignment.LeftCenter);
        }
    }

    // 合成参数回显轨（只读，voice 级一等轨）：遍历声明的回显轨，对每条可见轨用 Part.SynthesizedParameters
    // 的同名曲线绘制为半透明积分面积（曲线与底部基线围成的填充区），用各自 config 色——区别于可编辑轨的细线。
    // 显隐由宿主标题栏管控（不入分源 tabbar）；回显恒只读、不可编辑。effect 无参数回显（输出仅音频）。
    void DrawReadbackParameters(DrawingContext context)
    {
        if (Part == null)
            return;

        foreach (var kvp in mDependency.ReadbackConfigs)
        {
            AutomationKey key = kvp.Key;
            if (!mDependency.IsReadbackVisible(key))
                continue;

            // 按源取曲线数据：effect 走该 effect 的聚合回显，voice 走 part 自身回显。
            var data = key.IsEffect
                ? (key.EffectIndex < Part.Effects.Count ? Part.GetEffectSynthesizedParameters(Part.Effects[key.EffectIndex]) : null)
                : Part.SynthesizedParameters;
            if (data == null || !data.TryGetValue(key.Id, out var parameter))
                continue;

            var config = kvp.Value.Config;
            DrawReadbackArea(context, parameter.Segments, config.MinValue, config.MaxValue, Color.Parse(config.Color));
        }
    }

    // 一条回显轨：各分段（按段交付，段间空、跨段不连线）各自绘制为独立填充面积（仅面积、无描边）。
    void DrawReadbackArea(DrawingContext context, IReadOnlyList<IReadOnlyList<Foundation.Point>> segments, double min, double max, Color color)
    {
        var tempoManager = Part!.TempoManager;
        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;
        var fillBrush = color.Opacity(0.25).ToBrush();
        double baselineY = Bounds.Height;

        foreach (var segment in segments)
        {
            if (segment.Count == 0)
                continue;

            double startTime = segment[0].X;
            double endTime = segment[segment.Count - 1].X;
            double startTick = tempoManager.GetTick(startTime);
            double endTick = tempoManager.GetTick(endTime);
            if (endTick < minVisibleTick)
                continue;

            if (startTick > maxVisibleTick)
                break;

            int startX = (int)Math.Floor(TickAxis.Tick2X(Math.Max(startTick, minVisibleTick)));
            int endX = (int)Math.Ceiling(TickAxis.Tick2X(Math.Min(endTick, maxVisibleTick)));
            int count = endX - startX + 1;
            if (count <= 0)
                continue;

            var times = new double[count];
            for (int i = 0; i < count; i++)
                times[i] = tempoManager.GetTime(TickAxis.X2Tick(i + startX));

            var ys = segment.LinearInterpolation(times);
            var points = new Point[count];
            for (int i = 0; i < count; i++)
                points[i] = new(i + startX, ValueToY(ys[i], min, max));

            context.FillCurveArea(points, baselineY, fillBrush);
        }
    }

    // 范围选区（与音符区共用的同一条 tick 带）：白罩 + 纯白虚线，贯穿参数区全高。状态归 PianoScrollView，
    // 在此读 CurrentRegionSelection 绘制；参数区自身也可 Shift+拖建区（见 OnMouseDown 的 RegionSelectionOperation）。
    void DrawRegionSelection(DrawingContext context)
    {
        if (mDependency.PianoScrollView.CurrentRegionSelection is not { } region)
            return;

        double left = TickAxis.Tick2X(region.StartTick);
        double right = TickAxis.Tick2X(region.EndTick);
        // 白罩 + 左右两条纯白虚线竖边（不画上下横边）：与音符区那段拼成一条连续竖带、无横切、不叠画。
        context.FillRectangle(GUI.Style.WHITE.Opacity(0.12).ToBrush(), new Rect(left, 0, right - left, Bounds.Height));
        var pen = new Pen(GUI.Style.WHITE.ToUInt32(), 1) { DashStyle = DashStyle.Dash };
        context.DrawLine(pen, new Point(left, 0), new Point(left, Bounds.Height));
        context.DrawLine(pen, new Point(right, 0), new Point(right, Bounds.Height));
    }

    // 音素段可越出所属 note 几何（前置辅音向前伸、元音向满末/melisma 充填）：note 级可见性/命中过滤
    // 只能做宽松截断，本值是前伸量的保守上界（秒）。
    const double LeadExtensionSlack = 2;

    // note lane 的呈现/命中区间（全局 tick）：起点 = note 起点，终点 = min(note 满末, 下一 note 起点)——
    // 展示宽度去重叠，镜像合成侧「后盖前」钳位口径（尾巴钳到数据序下一 note 起点、起点从不动）；
    // 钳后无宽度者（被完全覆盖 / 同起点和弦兄弟）不出段。渲染与编辑命中共用本口径，所见即所改。
    static IEnumerable<(INote Note, double StartTick, double EndTick)> NoteLaneRanges(IMidiPart part)
    {
        for (var note = part.Notes.First; note != null; note = note.Next)
        {
            double startTick = note.GlobalStartPos();
            double endTick = note.GlobalEndPos();
            if (note.Next != null)
                endTick = Math.Min(endTick, note.Next.GlobalStartPos());
            if (endTick <= startTick)
                continue;
            yield return (note, startTick, endTick);
        }
    }

    public Point TickAndValueToPoint(double tick, double value, double min, double max)
    {
        double partPos = Part?.Pos.Value ?? 0;
        return new(TickAxis.Tick2X(partPos + tick), ValueToY(value, min, max));
    }

    public double ValueToY(double value, double min, double max)
    {
        double range = max - min;
        if (range == 0)
            return Bounds.Height / 2;

        return (max - value.Limit(min, max)) * (Bounds.Height / range);
    }

    public double YToValue(double y, double min, double max)
    {
        double height = Bounds.Height;
        if (height == 0)
            return min;

        return (max - (y / height) * (max - min)).Limit(min, max);
    }

    public bool HasSelectedAnchors()
    {
        if (TryGetActiveAutomation(out var automation, out _, false))
            return automation.Points.HasSelectedItem();

        if (TryGetActivePiecewise(out var piecewise, out _, false))
            return piecewise.AnchorGroups.Any(group => group.HasSelectedItem());

        return false;
    }

    public void SelectAllAnchors()
    {
        if (Part == null)
            return;

        Part.Pitch.DeselectAllAnchors();
        mDependency.PianoScrollView.InvalidateVisual();
        if (TryGetActiveAutomation(out var automation, out _, false))
            automation.Points.SelectAllItems();
        else if (TryGetActivePiecewise(out var piecewise, out _, false))
            piecewise.SelectAllAnchors();

        InvalidateVisual();
        UpdateAnchorValueInput();
    }

    public void DeleteSelectedAnchors()
    {
        if (Part == null)
            return;

        if (TryGetActiveAutomation(out var automation, out _, false))
        {
            var selectedPoints = automation.Points.AllSelectedItems().ToList();
            if (selectedPoints.IsEmpty())
                return;

            automation.DeletePoints(selectedPoints);
        }
        else if (TryGetActivePiecewise(out var piecewise, out _, false))
        {
            if (!piecewise.AnchorGroups.Any(group => group.HasSelectedItem()))
                return;

            piecewise.DeleteAllSelectedAnchors();
        }
        else
        {
            return;
        }

        Part.Commit();
        InvalidateVisual();
        UpdateAnchorValueInput();
    }

    public void RefreshAnchorValueInput()
    {
        UpdateAnchorValueInput();
    }

    void UpdateAnchorValueInput()
    {
        UpdateAnchorValueInput(true);
    }

    void UpdateAnchorValueInput(bool invalidateArrange)
    {
        if (!TryGetAnchorValueInputTarget(out var automation, out var anchor, out _, out _))
        {
            if (mAnchorValueInput.IsFocused)
            {
                mIgnoreAnchorValueInputEndInput = true;
                mAnchorValueInput.Unfocus();
            }
            mAnchorValueInput.IsVisible = false;
            if (invalidateArrange)
                InvalidateArrange();
            return;
        }

        mAnchorValueInput.IsVisible = true;
        if (!mAnchorValueInput.IsFocused)
            mAnchorValueInput.Display(AnchorValueToString(anchor.Value + automation.DefaultValue.Value));
        if (invalidateArrange)
            InvalidateArrange();
    }

    void OnAnchorValueInputEndInput()
    {
        if (mIgnoreAnchorValueInputEndInput)
        {
            mIgnoreAnchorValueInputEndInput = false;
            UpdateAnchorValueInput();
            return;
        }

        if (!TryGetAnchorValueInputTarget(out var automation, out var anchor, out var config, out _))
        {
            UpdateAnchorValueInput();
            return;
        }

        var text = mAnchorValueInput.Text.Trim();
        if (!double.TryParse(text, out var value))
        {
            UpdateAnchorValueInput();
            return;
        }

        value = value.Limit(config.MinValue, config.MaxValue);
        double currentValue = anchor.Value + automation.DefaultValue.Value;
        double valueOffset = value - currentValue;
        if (valueOffset == 0)
        {
            UpdateAnchorValueInput();
            return;
        }

        var part = Part;
        if (part == null)
        {
            UpdateAnchorValueInput();
            return;
        }

        part.BeginMergeDirty();
        automation.MoveSelectedPoints(0, valueOffset);
        part.EndMergeDirty();
        part.Commit();
        InvalidateVisual();
        UpdateAnchorValueInput();
    }

    void OnAnchorValueInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            mIgnoreAnchorValueInputEndInput = true;
            mAnchorValueInput.Unfocus();
        }
    }

    void OnAnchorValueInputGotFocus(object? sender, GotFocusEventArgs e)
    {
        SelectAllAnchorValueInputText();
    }

    void OnAnchorValueInputPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        mAnchorValueInput.Focus();
        SelectAllAnchorValueInputText();
        e.Handled = true;
    }

    void SelectAllAnchorValueInputText()
    {
        Dispatcher.UIThread.Post(mAnchorValueInput.SelectAll, DispatcherPriority.Background);
    }

    bool TryGetAnchorValueInputTarget(out IAutomation automation, out AnchorPoint anchor, out AutomationConfig config, out Point position)
    {
        automation = null!;
        anchor = null!;
        config = null!;
        position = default;

        if (mDependency.PianoTool.Value != PianoTool.Anchor)
            return false;

        if (!TryGetActiveAutomation(out var activeAutomation, out var activeConfig, false))
            return false;

        var selectedAnchor = activeAutomation.Points.AllSelectedItems().OrderBy(point => point.Pos).FirstOrDefault();
        if (selectedAnchor == null)
            return false;

        var selectedPosition = TickAndValueToPoint(selectedAnchor.Pos, selectedAnchor.Value + activeAutomation.DefaultValue.Value, activeConfig.MinValue, activeConfig.MaxValue);
        if (selectedPosition.X < 0 || selectedPosition.X > Bounds.Width || selectedPosition.Y < 0 || selectedPosition.Y > Bounds.Height)
            return false;

        automation = activeAutomation;
        anchor = selectedAnchor;
        config = activeConfig;
        position = selectedPosition;
        return true;
    }

    bool TryGetAnchorValueInputRect(out Rect rect)
    {
        rect = default;
        if (!TryGetAnchorValueInputTarget(out _, out _, out _, out var position))
            return false;

        double maxLeft = Math.Max(0, Bounds.Width - AnchorValueInputWidth);
        double left = (position.X - AnchorValueInputWidth / 2).Limit(0, maxLeft);
        double top = Math.Max(0, position.Y - AnchorValueInputHeight - AnchorValueInputGap);
        rect = new(left, top, AnchorValueInputWidth, AnchorValueInputHeight);
        return true;
    }

    static string AnchorValueToString(double value)
    {
        return value.ToString("+0.00;-0.00");
    }

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
    readonly TextInput mAnchorValueInput;
    bool mIgnoreAnchorValueInputEndInput = false;

    const double AnchorValueInputWidth = 64;
    const double AnchorValueInputHeight = 24;
    const double AnchorValueInputGap = 8;
}
