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
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

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
