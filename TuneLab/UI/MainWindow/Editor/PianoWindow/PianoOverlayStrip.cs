using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Animation;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;

using Rect = Avalonia.Rect;
using Size = Avalonia.Size;

namespace TuneLab.UI;

// 叠到钢琴窗上的画布的悬浮标签（PianoOverlay）：悬在音符区右上角、时间轴下方，列出叠着的画布（不分工具）。
// 只要这个 part 有能叠的画布就在。标签与参数区标签栏同一个按钮——
// · 单击 = 激活：临时画在最上、总是显示；再点一下撤掉激活。不改顺序，也不碰小眼睛。
// · 右键 = 小眼睛：不激活时显不显示（关了不画、不收事件，但还叠着）。
// · 拖动 = 排序：标签顺序就是叠放顺序，越靠左越在上（新叠上去的出现在最左）。手感同数组控件拖序号条、轨道头拖拽：被拖的跟手，
//   其余按新排布以指数缓动让位；全程只动 RenderTransform，松手才写顺序。按下不立即激活（EditOnClick），松开时没拖才算单击。
// 最右的「+」弹勾选列表（PianoOverlayMenu）加上 / 取下；最左的「>」把标签向右收起、只剩「<」，再点展开，收起展开带动画。
// 描一圈边，和底下的钢琴窗分开。
internal class PianoOverlayStrip : Border
{
    public PianoOverlayStrip(IHolder<IMidiPart> partHolder)
    {
        mPartHolder = partHolder;
        mDragAnimation = new TabDragAnimation(this);

        Background = Style.INTERFACE.Opacity(0.85).ToBrush();
        BorderBrush = Style.LIGHT_WHITE.Opacity(0.3).ToBrush();
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(4);
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;

        var hoverBack = Colors.White.Opacity(0.05);

        // 最左：收起 / 展开。
        mChevron = new IconItem() { Icon = Assets.ChevronRight };
        mCollapseButton = new TuneLab.GUI.Components.Button() { Width = 20, Height = 24 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
            .AddContent(new() { Item = mChevron, ColorSet = new() { Color = Style.LIGHT_WHITE } });
        mCollapseButton.Clicked += () => PianoOverlay.IsCollapsed = !PianoOverlay.IsCollapsed;

        // 「+」：叠哪些画布的勾选列表。建一次、一直留着：勾选列表锚在它上面、点一项不关，每勾一项标签就更新一遍——
        // 要是「+」也跟着重建，锚点离开界面，列表就被关掉了。
        mAddButton = new TuneLab.GUI.Components.Button() { Width = 24, Height = 24 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.Plus }, ColorSet = new() { Color = Colors.White } });
        mAddButton.SetupToolTip("Place on the Piano Roll".Tr(TC.Menu));
        // 列表开着时再点「+」= 收起列表（见 MenuButtonToggle）。
        MenuButtonToggle.Attach(mAddButton, () => mPartHolder.Value is { } part ? PianoOverlayMenu.Open(mAddButton, part) : null);

        var content = new StackPanel() { Orientation = Orientation.Horizontal, Margin = new(4, 0, 0, 0) };
        content.Children.Add(mTabs);
        content.Children.Add(mAddButton);
        mBody = new CollapsePanel() { Child = content };

        var layout = new StackPanel() { Orientation = Orientation.Horizontal };
        layout.Children.Add(mCollapseButton);
        layout.Children.Add(mBody);
        Child = layout;

        mCollapseAnimation.ValueChanged += value => mBody.Progress = value;
        ApplyCollapsed(animate: false);

        PianoOverlay.Changed.Subscribe(OnOverlayChanged, s);
        mPartHolder.Modified.Subscribe(Rebuild, s);
        mPartHolder.When(p => p.SoundSource.Modified).Subscribe(Rebuild, s);
        mPartHolder.When(p => p.Effects.MembershipModified).Subscribe(Rebuild, s);
        mPartHolder.When(p => p.AutomationConfigsModified).Subscribe(Rebuild, s);
        Rebuild();
    }

    ~PianoOverlayStrip()
    {
        s.DisposeAll();
    }

    void OnOverlayChanged()
    {
        if (mCollapsed != PianoOverlay.IsCollapsed)
            ApplyCollapsed(animate: true);
        Rebuild();
    }

    // 收起：标签与「+」向右缩进去（左边被裁掉），只剩「<」；展开反过来。
    void ApplyCollapsed(bool animate)
    {
        mCollapsed = PianoOverlay.IsCollapsed;
        mChevron.Icon = mCollapsed ? Assets.ChevronLeft : Assets.ChevronRight;
        mCollapseButton.InvalidateVisual();
        mCollapseButton.SetupToolTip((mCollapsed ? "Expand" : "Collapse").Tr(TC.Menu));
        mBody.IsHitTestVisible = !mCollapsed;
        double target = mCollapsed ? 0 : 1;
        if (animate)
            mCollapseAnimation.SetFromTo(mBody.Progress, target, CollapseMillisec, AnimationCurve.CubicOut);
        else
            mBody.Progress = target;
    }

    // 同参数区标签栏：按钮按画布缓存、只换状态（状态变化有颜色过渡）；成员或顺序变了才重排。每次都新建按钮的话，
    // 新按钮从"关"的颜色过渡到当前状态，切一下标签整排闪一下。
    void Rebuild()
    {
        CancelDrag();
        var part = mPartHolder.Value;
        var layers = part == null ? [] : PianoOverlay.Layers(part);
        IsVisible = part != null && (layers.Count > 0 || PianoOverlay.Candidates(part).Count > 0);
        mOrder.Clear();
        if (part == null || !IsVisible)
        {
            mTabs.Children.Clear();
            return;
        }

        mAddButton.Margin = new(layers.Count > 0 ? 4 : 0, 0, 0, 0);
        var buttons = new List<ParameterButton>(layers.Count);
        // 自上而下排：最上面的在最左（存储是自下而上，倒过来）。
        layers.Reverse();
        foreach (var layer in layers)
        {
            if (!mButtons.TryGetValue(layer.Key, out var button))
            {
                button = CreateButton(layer);
                mButtons.Add(layer.Key, button);
            }
            else
            {
                // 改色会掐断正在进行的状态过渡，真变了才改。
                var color = ColorUtils.ParseOrFallback(layer.Color);
                if (button.Color != color)
                    button.Color = color;
                if (button.Text != layer.Name)
                    button.Text = layer.Name;
            }
            button.State = layer.Active ? ParameterButton.ButtonState.Edit : layer.Shown ? ParameterButton.ButtonState.Visible : ParameterButton.ButtonState.Off;
            buttons.Add(button);
            mOrder.Add(layer.Key);
        }

        bool same = buttons.Count == mTabs.Children.Count;
        for (int i = 0; same && i < buttons.Count; i++)
            same = ReferenceEquals(buttons[i], mTabs.Children[i]);
        if (!same)
        {
            mTabs.Children.Clear();
            foreach (var button in buttons)
                mTabs.Children.Add(button);
        }
    }

    ParameterButton CreateButton(PianoOverlay.Layer layer)
    {
        var key = layer.Key;
        var button = new ParameterButton(ColorUtils.ParseOrFallback(layer.Color), layer.Name) { Margin = new(2, 0), EditOnClick = true };
        // 按此刻的 part 与按钮状态办（按钮是缓存的，不捕获建它那一刻的）。
        button.StateChangeAsked += state =>
        {
            if (mPartHolder.Value is not { } part)
                return;
            if (state == ParameterButton.ButtonState.Edit)
                PianoOverlay.SetActive(part, key, true);
            else if (button.State == ParameterButton.ButtonState.Edit)
                PianoOverlay.SetActive(part, key, false);
            else
                PianoOverlay.SetShown(part, key, state == ParameterButton.ButtonState.Visible);
        };

        // 拖动排序：按下记位置，移过阈值开始拖（拿走捕获，按钮里那次按下随之不算单击），松手写顺序；捕获丢了就回弹、不写。
        button.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                mPressed = button;
                mDownX = e.GetPosition(mTabs).X;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (!ReferenceEquals(mPressed, button))
                return;
            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                mPressed = null;   // 按键已在别处松开
                return;
            }
            double dx = e.GetPosition(mTabs).X - mDownX;
            if (mDrag == null)
            {
                if (Math.Abs(dx) < DragThreshold || !BeginDrag(button))
                    return;
                e.Pointer.Capture(button);
            }
            mDrag?.Update(dx);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (!ReferenceEquals(mPressed, button))
                return;
            mPressed = null;
            if (mDrag is not { } drag)
                return;
            mDrag = null;   // 先清再释放：释放触发的捕获丢失不当成取消
            e.Pointer.Capture(null);
            EndDrag(drag);
            e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.PointerCaptureLost += (_, e) =>
        {
            if (ReferenceEquals(e.Source, button) && mDrag != null)
                CancelDrag();
        };
        return button;
    }

    bool BeginDrag(ParameterButton button)
    {
        var tabs = mTabs.Children.OfType<ParameterButton>().ToList();
        int index = tabs.IndexOf(button);
        if (index < 0 || tabs.Count < 2)
            return false;
        mDrag = new TabDrag(tabs, index);
        button.ZIndex = 1;   // 浮在让位的兄弟之上
        AnimationManager.SharedManager.StartAnimation(mDragAnimation);
        return true;
    }

    void EndDrag(TabDrag drag)
    {
        AnimationManager.SharedManager.StopAnimation(mDragAnimation);
        var order = drag.FinalOrder().Select(i => mOrder[i]).Reverse().ToList();   // 标签自上而下 → 存储自下而上
        drag.Reset();
        if (drag.Delta != 0 && mPartHolder.Value is { } part)
            PianoOverlay.Reorder(part, order);   // 改了就发 Changed → Rebuild 按新顺序排布
    }

    void CancelDrag()
    {
        mPressed = null;
        if (mDrag is not { } drag)
            return;
        mDrag = null;
        AnimationManager.SharedManager.StopAnimation(mDragAnimation);
        drag.Reset();
    }

    // 横向的单个标签拖动（照数组控件的 RowDrag：被拖的跟手但不越出首尾，按前沿越过邻居中线定落点，其余按新排布缓动让位）。
    sealed class TabDrag
    {
        public int Delta { get; private set; }

        public TabDrag(IReadOnlyList<ParameterButton> tabs, int dragged)
        {
            mTabs = tabs;
            mDragged = dragged;
            mLefts = tabs.Select(t => t.Bounds.X - t.Margin.Left).ToArray();
            mWidths = tabs.Select(t => t.Bounds.Width + t.Margin.Left + t.Margin.Right).ToArray();
            mOffsets = new double[tabs.Count];
            mTargets = new double[tabs.Count];
            mFinalOrder = Enumerable.Range(0, tabs.Count).ToArray();
        }

        // mFinalOrder[槽] = 落在该槽的标签下标。
        public int[] FinalOrder() => mFinalOrder;

        // pointerDelta：指针相对按下处的横向位移。
        public void Update(double pointerDelta)
        {
            double left = mLefts[mDragged];
            double right = left + mWidths[mDragged];
            double end = mLefts[^1] + mWidths[^1];
            double offset = Math.Clamp(pointerDelta, mLefts[0] - left, end - right);
            Translate(mTabs[mDragged], offset);

            // 前沿越过邻居的中线即算越过（同数组控件）。
            int delta = 0;
            if (offset > 0)
            {
                for (int j = mDragged + 1; j < mTabs.Count; j++)
                    if (right + offset > mLefts[j] + mWidths[j] / 2)
                        delta++;
            }
            else if (offset < 0)
            {
                for (int j = mDragged - 1; j >= 0; j--)
                    if (left + offset < mLefts[j] + mWidths[j] / 2)
                        delta--;
            }
            Delta = delta;

            var order = new List<int>(Enumerable.Range(0, mTabs.Count));
            order.RemoveAt(mDragged);
            order.Insert(mDragged + delta, mDragged);
            mFinalOrder = order.ToArray();

            // 未被拖的标签的目标位移 = 新排布下的左缘 − 原左缘。
            double x = mLefts[0];
            foreach (var j in mFinalOrder)
            {
                mTargets[j] = j == mDragged ? 0 : x - mLefts[j];
                x += mWidths[j];
            }
        }

        // 让位缓动：各未被拖标签的位移指数逼近目标位移。
        public void Tick(double millisec)
        {
            double dt = double.IsNaN(mLastTickMs) ? FrameMs : Math.Max(0, millisec - mLastTickMs);
            mLastTickMs = millisec;
            double factor = 1 - Math.Exp(-dt / SmoothTau);
            for (int j = 0; j < mTabs.Count; j++)
            {
                if (j == mDragged)
                    continue;
                double next = mOffsets[j] + (mTargets[j] - mOffsets[j]) * factor;
                if (Math.Abs(next - mTargets[j]) < 0.5)
                    next = mTargets[j];
                if (next != mOffsets[j])
                {
                    mOffsets[j] = next;
                    Translate(mTabs[j], next);
                }
            }
        }

        public void Reset()
        {
            foreach (var tab in mTabs)
            {
                tab.RenderTransform = null;
                tab.ZIndex = 0;
            }
        }

        static void Translate(Control tab, double x) => tab.RenderTransform = new TranslateTransform(x, 0);

        const double SmoothTau = 45;   // 让位缓动时间常数（ms），同数组控件、轨道头
        const double FrameMs = 16;
        readonly IReadOnlyList<ParameterButton> mTabs;
        readonly int mDragged;
        readonly double[] mLefts;
        readonly double[] mWidths;
        readonly double[] mOffsets;
        readonly double[] mTargets;
        int[] mFinalOrder;
        double mLastTickMs = double.NaN;
    }

    sealed class TabDragAnimation(PianoOverlayStrip owner) : IAnimation
    {
        public void Update(double millisec) => owner.mDrag?.Tick(millisec);
    }

    // 收起动画用：宽度 = 内容宽 × Progress，内容贴右、左边被裁掉（向右缩进去）。
    sealed class CollapsePanel : Decorator
    {
        public double Progress
        {
            get => mProgress;
            set
            {
                mProgress = value;
                InvalidateMeasure();
            }
        }

        public CollapsePanel()
        {
            ClipToBounds = true;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Child == null)
                return default;
            Child.Measure(Size.Infinity);
            return new Size(Child.DesiredSize.Width * mProgress, Child.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(finalSize.Width - Child.DesiredSize.Width, 0, Child.DesiredSize.Width, finalSize.Height));
            return finalSize;
        }

        double mProgress = 1;
    }

    const double DragThreshold = 4;
    const double CollapseMillisec = 200;


    TabDrag? mDrag;
    ParameterButton? mPressed;
    double mDownX;
    bool mCollapsed;
    readonly TabDragAnimation mDragAnimation;
    readonly AnimationController mCollapseAnimation = new();
    readonly List<AutomationKey> mOrder = [];
    readonly Dictionary<AutomationKey, ParameterButton> mButtons = new();
    readonly StackPanel mTabs = new() { Orientation = Orientation.Horizontal };
    readonly CollapsePanel mBody;
    readonly IconItem mChevron;
    readonly TuneLab.GUI.Components.Button mCollapseButton;
    readonly TuneLab.GUI.Components.Button mAddButton;
    readonly IHolder<IMidiPart> mPartHolder;
    readonly DisposableManager s = new();
}

// 悬浮标签的「+」：叠哪些画布的勾选列表（打勾 = 叠在钢琴窗上，点一下切换、菜单不关）。
// 列表 = 这个 part 此刻能叠的全部画布（时间轴画布、画布 lane），按参数属于谁分组，同参数区标签栏的勾选列表。
internal static class PianoOverlayMenu
{
    public static ContextMenu Open(Control anchor, IMidiPart part)
    {
        var menu = new ContextMenu();
        string? group = null;
        foreach (var candidate in PianoOverlay.Candidates(part))
        {
            if (candidate.Group != group)
            {
                group = candidate.Group;
                menu.Items.Add(MenuSeparators.Labeled(group));
            }
            var key = candidate.Key;
            MenuItem? item = null;
            item = new MenuItem() { ToggleType = MenuItemToggleType.CheckBox, IsChecked = PianoOverlay.IsOverlaid(part, key), StaysOpenOnClick = true }
                .SetName(candidate.Name)
                .SetAction(() =>
                {
                    PianoOverlay.SetOverlaid(part, key, !PianoOverlay.IsOverlaid(part, key));
                    // 菜单项点击时自己也翻转勾选态，先后不定：推迟到这次点击处理完再按存储回填（同标签栏的勾选列表）。
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => item!.IsChecked = PianoOverlay.IsOverlaid(part, key));
                });
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem() { IsEnabled = false }.SetName("No canvas can be placed on the piano roll".Tr(TC.Menu)));
        else
            menu.Items.Insert(0, new MenuItem() { IsEnabled = false }.SetName("Place on the Piano Roll".Tr(TC.Menu)));

        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.BottomEdgeAlignedRight;
        menu.Open(anchor);
        return menu;
    }
}
