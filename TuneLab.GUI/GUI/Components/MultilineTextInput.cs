using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using TuneLab.Animation;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

// 多行文本输入控件：基于 AvaloniaEdit 的 TextEditor（自带 TextView 排版体系），供各处需要多行输入的场景复用
// （歌词录入、Agent 输入框等）。相较自定义 TextInput（继承原生 TextBox）：TextBox 在 TextWrapping=Wrap 下有
// 框架层选择重排 bug——从一个软换行的行首往前选，上一行末字会被拽到下一行；TextEditor 走独立排版，不受此累。
// 实现 IDataValueController<string> 与 TextInput 同构，可直接接属性绑定管线（多行场景通常不作多选字段用，
// 故 DisplayNull/DisplayMultiple 退化为清空）。
internal class MultilineTextInput : TextEditor, IDataValueController<string>
{
    public IActionEvent EnterInput => mEnterInput;
    public new IActionEvent TextChanged => mTextChanged;
    public IActionEvent EndInput => mEndInput;

    public IActionEvent ValueWillChange => EnterInput;
    public IActionEvent ValueChanged => TextChanged;
    public IActionEvent ValueCommitted => EndInput;
    public string Value => Text ?? string.Empty;

    public MultilineTextInput()
    {
        MinWidth = 0;
        MinHeight = 0;

        WordWrap = true;   // 自动换行（多行输入本意；横向不滚）
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        // 隐藏原生 Fluent 竖条（仍可滚），改挂统一浮层滚动条（贴边细条、靠近才浮现）。
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;

        Background = Style.BACK.ToBrush();
        Foreground = Style.TEXT_NORMAL.ToBrush();
        FontSize = 12;
        Padding = new(8, 8);
        BorderThickness = new(0);
        // 选中样式对齐单行 TextInput：浅底(LIGHT_WHITE) + 深字(BACK)。
        TextArea.SelectionBrush = Style.LIGHT_WHITE.ToBrush();
        TextArea.SelectionForeground = Style.BACK.ToBrush();

        // 自定义占位符：在 TextView 画布上、按正文同一基线绘制（见 PlaceholderRenderer）。不用 TextEditor 内置 Watermark——
        // 那是 TextArea 模板里的独立 TextBlock，行高/基线与正文 TextView 略不同（实测差约 0.5px），空框时与真实文字不同高。
        TextArea.TextView.BackgroundRenderers.Add(new PlaceholderRenderer(this));

        base.TextChanged += (s, e) => mTextChanged.Invoke();
        // 焦点落在内部 TextArea 上：进入=开始编辑（ValueWillChange），退出=提交（ValueCommitted）。
        TextArea.GotFocus += (s, e) => mEnterInput.Invoke();
        TextArea.LostFocus += (s, e) => mEndInput.Invoke();

        // 统一滚动条：绑 AvaloniaEdit 滚动的 IScrollAxis 适配器，经 AdornerLayer 叠在文本框上。adorner 拿不到
        // 自身指针事件，故用 AttachInput 让它从本控件（隧道）接管输入。普通文本框常驻显示（可滚才画手柄），
        // 不做"靠近才显"。
        mScrollBar = new(new TextEditorScrollAxis(this), Orientation.Vertical);
        mScrollBar.AttachInput(this);
        AttachedToVisualTree += OnAttachedForScrollBar;
        DetachedFromVisualTree += OnDetachedForScrollBar;

        // 平滑滚轮：隧道阶段拦截（抢在 AvaloniaEdit 原生逐行跳滚之前），指数缓动 ScrollViewer 偏移，
        // 与钢琴/编排区滚轮手感一致。无可滚内容时放行、让事件冒泡到外层可滚容器。
        mScrollAnimation = new WheelScrollAnimation(this);
        AddHandler(PointerWheelChangedEvent, OnWheelScroll, RoutingStrategies.Tunnel);
    }

    void OnWheelScroll(object? sender, PointerWheelEventArgs e)
    {
        var sv = this.FindDescendantOfType<ScrollViewer>();
        if (sv == null)
            return;

        double max = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        if (max <= 0)
            return;   // 无可滚内容：放行冒泡（外层容器可接管）

        double baseOffset = mWheelAnimating ? mWheelTarget : sv.Offset.Y;
        mWheelTarget = Math.Clamp(baseOffset - e.Delta.Y * WheelStep, 0, max);
        mLastWheelMs = double.NaN;
        mWheelAnimating = true;
        AnimationManager.SharedManager.StartAnimation(mScrollAnimation);
        e.Handled = true;
    }

    void TickWheelScroll(double millisec)
    {
        var sv = this.FindDescendantOfType<ScrollViewer>();
        if (sv == null)
        {
            mWheelAnimating = false;
            AnimationManager.SharedManager.StopAnimation(mScrollAnimation);
            return;
        }

        double dt = double.IsNaN(mLastWheelMs) ? 16 : Math.Max(0, millisec - mLastWheelMs);
        mLastWheelMs = millisec;

        double cur = sv.Offset.Y;
        double k = 1 - Math.Exp(-dt / WheelTau);
        double next = cur + (mWheelTarget - cur) * k;
        if (Math.Abs(next - mWheelTarget) < 0.5)
            next = mWheelTarget;

        if (next != cur)
            sv.Offset = new Vector(sv.Offset.X, next);

        if (next == mWheelTarget)
        {
            mWheelAnimating = false;
            AnimationManager.SharedManager.StopAnimation(mScrollAnimation);
        }
    }

    sealed class WheelScrollAnimation(MultilineTextInput owner) : IAnimation
    {
        public void Update(double millisec) => owner.TickWheelScroll(millisec);
    }

    void OnAttachedForScrollBar(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer == null || layer.Children.Contains(mScrollBar))
            return;

        AdornerLayer.SetAdornedElement(mScrollBar, this);
        layer.Children.Add(mScrollBar);
    }

    void OnDetachedForScrollBar(object? sender, VisualTreeAttachmentEventArgs e)
    {
        (mScrollBar.Parent as AdornerLayer)?.Children.Remove(mScrollBar);
    }

    // 把 AvaloniaEdit 的滚动适配成 IScrollAxis，供统一滚动条复用。只做纵向（WordWrap 下无横向滚动）。
    // 直接操作模板里的内部 ScrollViewer（标准滚动模型：Offset 设即滚、自带钳制）——TextEditor 的
    // ScrollToVerticalOffset/VerticalOffset 等 facade 实测拖动时不生效/读值恒 0。ScrollViewer 在模板套用后
    // 才存在，故懒解析。
    sealed class TextEditorScrollAxis : IScrollAxis
    {
        public event Action? AxisChanged;
        public double ViewLength { get => ScrollViewer?.Viewport.Height ?? 0; set { } }
        public double ContentLength => ScrollViewer?.Extent.Height ?? 0;
        public double ViewOffset
        {
            get => ScrollViewer?.Offset.Y ?? 0;
            set { var sv = ScrollViewer; if (sv != null) sv.Offset = new Vector(sv.Offset.X, value); }
        }

        public TextEditorScrollAxis(TextEditor editor)
        {
            mEditor = editor;
            var textView = editor.TextArea.TextView;
            textView.ScrollOffsetChanged += (s, e) => AxisChanged?.Invoke();   // 滚动
            textView.VisualLinesChanged += (s, e) => AxisChanged?.Invoke();    // 内容/排版变（打字增行等）
            editor.SizeChanged += (s, e) => AxisChanged?.Invoke();             // 视口尺寸变
        }

        ScrollViewer? ScrollViewer => mScrollViewer ??= mEditor.FindDescendantOfType<ScrollViewer>();

        readonly TextEditor mEditor;
        ScrollViewer? mScrollViewer;
    }

    // 子类须借 TextEditor 的 ControlTheme（模板按 key=typeof(TextEditor) 注册）：Avalonia 默认按控件自身类型找模板，
    // 不 override 则 MultilineTextInput 匹配不到模板、整个控件不渲染（无 Border/ScrollViewer/TextArea，全空不可见）。
    protected override Type StyleKeyOverride => typeof(TextEditor);

    // 占位符文本（空文档时显示）。用 new 隐藏 TextEditor 内置 Watermark：调用方设本属性即走自绘、不触发内置 TextBlock。
    public new string? Watermark
    {
        get => mWatermark;
        set { mWatermark = value; TextArea.TextView.InvalidateLayer(KnownLayer.Background); }
    }

    // 随内容自增长高度：开启后高度恒 = 内容实测高(DocumentHeight) + 上下对称内边距，超 MaxHeight 才封顶并内部滚动。
    // 框紧贴内容 + 对称内边距 → 内容整块（单行或多行）在框内垂直居中；再由调用方给控件设 VerticalAlignment=Center
    // 使这个"紧身"框在其输入行里整体居中。（不按光标居中：多行时会把末行光标拉到中央、反把整块文字顶到最上。）
    // 关闭（默认）时用固定 Height（歌词框等文本域范式），控件行为与普通 TextEditor 无异。
    public bool AutoGrow
    {
        get => mAutoGrow;
        set
        {
            if (mAutoGrow == value)
                return;
            mAutoGrow = value;
            if (value)
            {
                TextArea.TextView.VisualLinesChanged += OnVisualLinesChanged;
                UpdateAutoHeight();
            }
            else
            {
                TextArea.TextView.VisualLinesChanged -= OnVisualLinesChanged;
            }
        }
    }

    void OnVisualLinesChanged(object? sender, EventArgs e) => UpdateAutoHeight();

    void UpdateAutoHeight()
    {
        if (!mAutoGrow)
            return;

        // 框高紧贴内容：DocumentHeight（全部可视行排版总高，含软换行折叠）+ 上下对称内边距（调用方设的 Padding），无多余空隙 → 内容居中。
        var target = TextArea.TextView.DocumentHeight + Padding.Top + Padding.Bottom;
        var max = MaxHeight;
        if (!double.IsNaN(max) && !double.IsInfinity(max) && max > 0 && target > max)
            target = max;   // 封顶：超出则内部滚动

        // 仅在明显变化时改高：置 Height 会触发重排、可能回灌 VisualLinesChanged，靠阈值收敛避免自激。
        if (Math.Abs(Height - target) > 0.5)
            Height = target;
    }

    // 编辑中（聚焦）不被外部刷新覆盖：语义同 TextInput.Display，避免多选扇出/他处刷新中途重置光标。
    public void Display(string text)
    {
        if (TextArea.IsFocused)
            return;
        Text = text ?? string.Empty;
    }

    public void DisplayNull()
    {
        if (TextArea.IsFocused)
            return;
        Text = string.Empty;
    }

    public void DisplayMultiple()
    {
        if (TextArea.IsFocused)
            return;
        Text = string.Empty;
    }

    bool mAutoGrow;
    string? mWatermark;

    readonly ScrollBar mScrollBar;

    readonly WheelScrollAnimation mScrollAnimation;
    bool mWheelAnimating;
    double mWheelTarget;
    double mLastWheelMs = double.NaN;
    const double WheelStep = 50;   // 每格滚轮位移（px）
    const double WheelTau = 60;    // 缓动时间常数（ms）

    readonly ActionEvent mEnterInput = new();
    readonly ActionEvent mTextChanged = new();
    readonly ActionEvent mEndInput = new();

    // 空文档占位符：直接在 TextView 背景层绘制，坐标/字体/基线与正文同一套（正文基线在行顶下 DefaultBaseline 处），
    // 故占位符与真实输入文字逐像素同位——避免内置 Watermark(独立 TextBlock)与正文两套文本引擎的基线错位。
    sealed class PlaceholderRenderer(MultilineTextInput owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (string.IsNullOrEmpty(owner.mWatermark))
                return;
            if (textView.Document != null && textView.Document.TextLength > 0)
                return;

            var typeface = new Typeface(owner.FontFamily, owner.FontStyle, owner.FontWeight);
            var text = new FormattedText(owner.mWatermark, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, owner.FontSize, Style.LIGHT_WHITE.Opacity(0.5).ToBrush());
            // 令占位符基线 = 正文首行基线（行顶下 DefaultBaseline）：FormattedText 从顶部绘制，其 Baseline 为顶到基线距。
            drawingContext.DrawText(text, new Avalonia.Point(0, textView.DefaultBaseline - text.Baseline));
        }
    }
}
