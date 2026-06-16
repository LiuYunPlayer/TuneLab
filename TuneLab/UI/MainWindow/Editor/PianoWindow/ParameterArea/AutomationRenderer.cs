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
        IHolder<IMidiPart> PartHolder { get; }
        PianoScrollView PianoScrollView { get; }
        TickAxis TickAxis { get; }
        AutomationKey? ActiveAutomation { get; }
        bool IsAutomationVisible(AutomationKey automation);
        IReadOnlyList<AutomationKey> VisibleAutomations { get; }
        INotifiableProperty<PianoTool> PianoTool { get; }
    }

    public AutomationRenderer(IDependency dependency)
    {
        mDependency = dependency;

        mMiddleDragOperation = new(this);
        mDrawOperation = new(this);
        mClearOperation = new(this);
        mAnchorSelectOperation = new(this);
        mAnchorDeleteOperation = new(this);
        mAnchorMoveOperation = new(this);
        mVibratoAmplitudeOperation = new(this);

        mAnchorValueInput = new()
        {
            IsVisible = false,
            Width = AnchorValueInputWidth,
            Height = AnchorValueInputHeight,
            Padding = new(0),
            FontFamily = Assets.NotoMono,
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
        // effect 自动化数据在各 effect.Automations 里，编辑它不会触发 part.Automations.Modified，需单独订阅（否则拖动不重绘）。
        mDependency.PartHolder.When(p => p.Effects.WhenAny(effect => effect.Automations.Modified)).Subscribe(InvalidateVisual, s);
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
        mDependency.TickAxis.AxisChanged += Update;
        mDependency.TickAxis.AxisChanged += UpdateAnchorValueInput;
    }

    ~AutomationRenderer()
    {
        s.DisposeAll();
        mDependency.ActiveAutomationChanged -= InvalidateVisual;
        mDependency.ActiveAutomationChanged -= UpdateAnchorValueInput;
        mDependency.VisibleAutomationChanged -= InvalidateVisual;
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

        void Draw(AutomationKey automationID)
        {
            if (!Part.IsEffectiveAutomation(automationID))
                return;

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

            DrawSynthesizedParameter(context, automationID, min, max);
        }

        var activeAutomation = mDependency.ActiveAutomation;
        foreach (var automation in mDependency.VisibleAutomations)
        {
            if (automation == activeAutomation)
                continue;

            if (!mDependency.IsAutomationVisible(automation))
                continue;

            Draw(automation);
        }

        context.FillRectangle(Colors.Black.Opacity(0.25).ToBrush(), this.Rect());

        if (activeAutomation == null)
            return;

        var active = activeAutomation.Value;
        if (!Part.IsEffectiveAutomation(active))
            return;

        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;
        var config = Part.GetEffectiveAutomationConfig(active);
        double min = config.MinValue;
        double max = config.MaxValue;

        // effect 自动化无 vibrato 概念：vibrato 叠加层与"拖拽关联颤音"提示仅对 voice 轨绘制。
        if (!active.IsEffect)
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

                context.DrawCurve(points, Color.Parse(config.Color).Opacity(0.5), lineWidth);
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
        Draw(active);

        if (mAnchorSelectOperation.IsOperating)
        {
            var rect = mAnchorSelectOperation.SelectionRect();
            var selectionColor = GUI.Style.HIGH_LIGHT;
            context.DrawRectangle(selectionColor.Opacity(0.25).ToBrush(), new Pen(selectionColor.ToUInt32()), rect);
        }

        context.DrawString(max.ToString("+0.00;-0.00"), new Point(8, 12), Style.LIGHT_WHITE.ToBrush(), 12, Alignment.LeftCenter);
        context.DrawString(min.ToString("+0.00;-0.00"), new Point(8, Bounds.Height - 12), Style.LIGHT_WHITE.ToBrush(), 12, Alignment.LeftCenter);
    }

    // 合成参数回显：voice 引擎产物（按轨 id 键、与音频同一秒时间系，分段），只读叠加在同名 voice 轨上。
    // effect 轨无参数回显（effect 输出仅音频）。沿用 pitch 回显的白色半透明约定，区别于 config 色的可编辑曲线；
    // 段间空（NaN）= 各段独立断开（已按段交付，跨段不连线）。
    void DrawSynthesizedParameter(DrawingContext context, AutomationKey automation, double min, double max)
    {
        if (Part == null || automation.IsEffect)
            return;

        if (!Part.SynthesizedParameters.TryGetValue(automation.Id, out var segments))
            return;

        var tempoManager = Part.TempoManager;
        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;
        var color = Colors.White.Opacity(0.5);

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

            context.DrawCurve(points, color, 1);
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
        if (!TryGetActiveAutomation(out var automation, out _, false))
            return false;

        return automation.Points.HasSelectedItem();
    }

    public void SelectAllAnchors()
    {
        if (Part == null)
            return;

        Part.Pitch.DeselectAllAnchors();
        mDependency.PianoScrollView.InvalidateVisual();
        if (!TryGetActiveAutomation(out var automation, out _, false))
            return;

        automation.Points.SelectAllItems();
        InvalidateVisual();
        UpdateAnchorValueInput();
    }

    public void DeleteSelectedAnchors()
    {
        if (Part == null)
            return;

        if (!TryGetActiveAutomation(out var automation, out _, false))
            return;

        var selectedPoints = automation.Points.AllSelectedItems().ToList();
        if (selectedPoints.IsEmpty())
            return;

        automation.DeletePoints(selectedPoints);
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
