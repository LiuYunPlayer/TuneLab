using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Animation;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.Extensions.Voices;
using Avalonia.Media;
using TuneLab.GUI.Input;
using TuneLab.GUI.Components;
using Avalonia;
using TuneLab.Base.Event;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;
using TuneLab.Utils;
using Avalonia.Controls;
using TuneLab.I18N;
using Avalonia.Threading;

namespace TuneLab.UI;

internal partial class AutomationRenderer : View
{
    public TickAxis TickAxis => mDependency.TickAxis;
    public IMidiPart? Part => mDependency.PartProvider.Object;
    public bool IsOperating => mState != State.None;

    public interface IDependency
    {
        event Action? ActiveAutomationChanged;
        event Action? VisibleAutomationChanged;
        IProvider<IMidiPart> PartProvider { get; }
        PianoScrollView PianoScrollView { get; }
        TickAxis TickAxis { get; }
        string? ActiveAutomation { get; }
        bool IsAutomationVisible(string automationID);
        IReadOnlyList<string> VisibleAutomations { get; }
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

        mDependency.PartProvider.ObjectChanged.Subscribe(InvalidateVisual, s);
        mDependency.PartProvider.When(p => p.Automations.Modified).Subscribe(InvalidateVisual, s);
        mDependency.PartProvider.When(p => p.Vibratos.Modified).Subscribe(InvalidateVisual, s);
        mDependency.PartProvider.When(p => p.Pos.Modified).Subscribe(InvalidateVisual, s);
        mDependency.PartProvider.ObjectChanged.Subscribe(UpdateAnchorValueInput, s);
        mDependency.PartProvider.When(p => p.Automations.Modified).Subscribe(UpdateAnchorValueInput, s);
        mDependency.PartProvider.When(p => p.Pos.Modified).Subscribe(UpdateAnchorValueInput, s);
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

        void Draw(string automationID)
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

        if (!Part.IsEffectiveAutomation(activeAutomation))
            return;

        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;
        var config = Part.GetEffectiveAutomationConfig(activeAutomation);
        double min = config.MinValue;
        double max = config.MaxValue;
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

            double[] values = Part.GetAutomationValues(vticks, activeAutomation);

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
            if (!vibratoItem.Vibrato.AffectedAutomations.ContainsKey(activeAutomation))
            {
                var vibrato = vibratoItem.Vibrato;
                double x = TickAxis.Tick2X(vibrato.GlobalStartPos() + vibrato.Dur / 2);
                context.DrawString("Drag to associate the vibrato".Tr(this), new Point(x, 8), Brushes.White, 12, Alignment.CenterTop);
            }
        }

        lineWidth = 2;
        Draw(activeAutomation);

        if (mAnchorSelectOperation.IsOperating)
        {
            var rect = mAnchorSelectOperation.SelectionRect();
            var selectionColor = GUI.Style.HIGH_LIGHT;
            context.DrawRectangle(selectionColor.Opacity(0.25).ToBrush(), new Pen(selectionColor.ToUInt32()), rect);
        }

        context.DrawString(max.ToString("+0.00;-0.00"), new Point(8, 12), Style.LIGHT_WHITE.ToBrush(), 12, Alignment.LeftCenter);
        context.DrawString(min.ToString("+0.00;-0.00"), new Point(8, Bounds.Height - 12), Style.LIGHT_WHITE.ToBrush(), 12, Alignment.LeftCenter);
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
