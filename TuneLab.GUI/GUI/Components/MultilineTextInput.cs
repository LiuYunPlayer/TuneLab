using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

// 多行文本输入控件：基于 AvaloniaEdit 的 TextEditor（自带 TextView 排版体系），供各处需要多行输入的场景复用
// （歌词录入、Agent 输入框等）。相较自定义 TextInput（继承原生 TextBox）：TextBox 在 TextWrapping=Wrap 下有
// 框架层选择重排 bug——从一个软换行的行首往前选，上一行末字会被拽到下一行；TextEditor 走独立排版，不受此累。
// 实现 IDataValueController<string> 与 TextInput 同构，可直接接属性绑定管线，三态（有值/无值/多值）语义
// 也与单行一致——TextBoxConfig 声明 IsMultiline 后，属性面板用它替下单行控件，多选扇出照样成立。
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
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;   // 隐藏原生条，改挂统一浮层滚动条

        Background = Style.BACK.ToBrush();
        Foreground = Style.TEXT_NORMAL.ToBrush();
        FontSize = 12;
        // 横向内边距 = 滚动条预留厚度：右侧让文字不被竖条压住，左侧等宽保持对称。
        Padding = new(ScrollBar.ReservedThickness, 8);
        BorderThickness = new(0);
        // 选中样式对齐单行 TextInput：浅底(LIGHT_WHITE) + 深字(BACK)。
        TextArea.SelectionBrush = Style.LIGHT_WHITE.ToBrush();
        TextArea.SelectionForeground = Style.BACK.ToBrush();

        // 自定义占位符：在 TextView 画布上、按正文同一基线绘制（见 PlaceholderRenderer）。不用 TextEditor 内置 Watermark——
        // 那是 TextArea 模板里的独立 TextBlock，行高/基线与正文 TextView 略不同（实测差约 0.5px），空框时与真实文字不同高。
        TextArea.TextView.BackgroundRenderers.Add(new PlaceholderRenderer(this));

        // 只有【用户输入】才对外发 ValueChanged。AvaloniaEdit 的 TextChanged 对程序化赋值同样触发，
        // 而绑定管线把 ValueChanged 当成"用户在改"（DiscardTo + Set）——于是 Refresh() 回灌值时会在
        // 提交流程中途重入绑定并抛异常（失焦提交 → 通知 → Refresh → set_Text → TextChanged → DiscardTo）。
        // 单行的 TextInput 早就分开了这两件事（Display 走 base.Text，事件只在 OnKeyDown/OnTextInput 发），这里对齐它。
        base.TextChanged += (s, e) => { if (!mProgrammaticWrite) mTextChanged.Invoke(); };
        // 焦点落在内部 TextArea 上：进入=开始编辑（ValueWillChange），退出=提交（ValueCommitted）。
        TextArea.GotFocus += (s, e) => mEnterInput.Invoke();
        TextArea.LostFocus += (s, e) => mEndInput.Invoke();

        // 统一浮层滚动条（仅纵向，WordWrap 无横向）+ 平滑滚轮：经 AdornerLayer 叠 ScrollBar、由本控件代管输入。
        // 常驻显示（可滚才画手柄），不做"靠近才显"。cursorElement=TextArea：悬停手柄时把 i-beam 切成箭头。
        mScrollBars = new OverlayScrollBars(this, horizontal: false, vertical: true, cursorElement: TextArea.TextView);
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

        // 行数上限先落成真实的 MaxHeight（行高取不到时按字号估，见 LineHeight）。
        UpdateMaxHeightFromLines();

        // 框高紧贴内容：DocumentHeight（全部可视行排版总高，含软换行折叠）+ 上下对称内边距（调用方设的 Padding），无多余空隙 → 内容居中。
        var target = TextArea.TextView.DocumentHeight + Padding.Top + Padding.Bottom;
        var max = MaxHeight;
        if (!double.IsNaN(max) && !double.IsInfinity(max) && max > 0 && target > max)
            target = max;   // 封顶：超出则内部滚动

        // 仅在明显变化时改高：置 Height 会触发重排、可能回灌 VisualLinesChanged，靠阈值收敛避免自激。
        // 【首次必须单独判 NaN】Height 未赋值时是 NaN，而任何与 NaN 的比较都是 false——只写阈值判断的话
        // 这里一次都不会执行（实测：日志里 height 恒为 NaN），"框高紧贴内容"从来只是句空话，
        // 真正在起作用的是 TextEditor 按内容测量的天然高度 + MaxHeight 封顶。
        if (double.IsNaN(Height) || Math.Abs(Height - target) > 0.5)
            Height = target;
    }

    // 行数上限：框贴着内容长（一行内容就一行高，不占着几行空白），到 maxLines 行封顶、其后框内滚动。
    // 【是上限不是定高】——定高会让只有一行的字段白占四行；封顶后仍能滚，够得着后面的内容。
    // lines <= 0 = 不封顶：框随内容一直长高（此时若调用方另设了 MaxHeight，仍按它封顶）。
    // 走既有的 AutoGrow 那条路（Agent 输入框同款），只是把封顶值由行数算出来。
    public void SetMaxVisibleLines(int lines)
    {
        mMaxVisibleLines = lines < 0 ? 0 : lines;
        AutoGrow = true;
        UpdateMaxHeightFromLines();
        UpdateAutoHeight();
    }

    // 行数上限 → MaxHeight。写进【属性】而不是只在算高时本地夹一下：那样布局系统本身没有任何约束，
    // 全靠我们每次把 Height 算对，漏一次框就长出去了（实测就是这么长出去的）。设成属性后，
    // 即使某一帧没算高，测量/排布也会照 MaxHeight 封顶。0 = 不封顶，此时把属性还给调用方（可能自设了 MaxHeight）。
    void UpdateMaxHeightFromLines()
    {
        if (mMaxVisibleLines <= 0)
            return;
        var max = LineHeight * mMaxVisibleLines + Padding.Top + Padding.Bottom;
        if (double.IsNaN(MaxHeight) || double.IsInfinity(MaxHeight) || Math.Abs(MaxHeight - max) > 0.5)
            MaxHeight = max;
    }

    // 一行的排版高度。优先问排版体系要，取不到就按字号估（实测本版 AvaloniaEdit 的 DefaultLineHeight
    // 在我们调用的时机始终取不到值，故走的一直是估算这一支；而估值 = 字号 × 1.35 与排版体系算出来的行高
    // 完全吻合——同一段文本 DocumentHeight 48.60 / 3 行 = 16.20 = 12 × 1.35，故估算安全）。
    double LineHeight
    {
        get
        {
            var h = TextArea.TextView.DefaultLineHeight;
            return double.IsNaN(h) || double.IsInfinity(h) || h <= 0 ? FontSize * 1.35 : h;
        }
    }

    // 编辑中（聚焦）不被外部刷新覆盖：语义同 TextInput.Display，避免多选扇出/他处刷新中途重置光标。
    public void Display(string text)
    {
        if (TextArea.IsFocused)
            return;
        SetMultipleHint(null);
        SetTextSilently(text ?? string.Empty);
    }

    public void DisplayNull()
    {
        if (TextArea.IsFocused)
            return;
        SetMultipleHint(null);
        SetTextSilently(string.Empty);
    }

    // 多值：与单行 TextInput 同款——正文留空、只以占位符提示，聚焦即从空白起编，不会编辑到 "(Multiple)" 字面量。
    // 但【不动调用方设的 Watermark】：那是这个框长期的提示语（如 Agent 输入框），不该被一次刷新擦掉，
    // 故多值提示单独存一格，绘制时优先于 Watermark。
    public void DisplayMultiple()
    {
        if (TextArea.IsFocused)
            return;
        SetMultipleHint("(Multiple)");
        SetTextSilently(string.Empty);
    }

    // 程序化写入：期间不对外发 ValueChanged（见构造函数里 TextChanged 那段注释）。
    void SetTextSilently(string text)
    {
        if (Text == text)
            return;
        mProgrammaticWrite = true;
        try { Text = text; }
        finally { mProgrammaticWrite = false; }
    }

    void SetMultipleHint(string? hint)
    {
        if (mMultipleHint == hint)
            return;
        mMultipleHint = hint;
        TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    bool mAutoGrow;
    bool mProgrammaticWrite;
    int mMaxVisibleLines;
    string? mWatermark;
    string? mMultipleHint;

    readonly OverlayScrollBars mScrollBars;

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
            var placeholder = owner.mMultipleHint ?? owner.mWatermark;   // 多值提示优先于长期占位符
            if (string.IsNullOrEmpty(placeholder))
                return;
            if (textView.Document != null && textView.Document.TextLength > 0)
                return;

            var typeface = new Typeface(owner.FontFamily, owner.FontStyle, owner.FontWeight);
            var text = new FormattedText(placeholder, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, owner.FontSize, Style.LIGHT_WHITE.Opacity(0.5).ToBrush());
            // 令占位符基线 = 正文首行基线（行顶下 DefaultBaseline）：FormattedText 从顶部绘制，其 Baseline 为顶到基线距。
            drawingContext.DrawText(text, new Avalonia.Point(0, textView.DefaultBaseline - text.Baseline));
        }
    }
}
