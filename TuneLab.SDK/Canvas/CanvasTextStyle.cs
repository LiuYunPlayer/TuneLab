using System;

namespace TuneLab.SDK;

// 文字排版：字号与对齐（字体由宿主决定，跟随界面字体）。DrawText、MeasureText、PopupTextBox 收同一个对象——
// 弹出文本框时把画这段字的那个样式与矩形原样传过去，框就正好盖在字上；三处都可以不传（null），等于 Create() 的默认样式。
// 不可变，构造函数全封，只走静态工厂 + With（以后加粗细、斜体等是加一个 With，不动绘制方法的签名）。可以建好存进静态字段反复用。
public sealed class CanvasTextStyle
{
    // 字号（逻辑像素）。null = 跟随宿主（与宿主界面上字段文字同档，宿主调整时随之变化）——所以排版要按 MeasureText
    // 量出来的尺寸，别按某个字号推算。
    public double? FontSize { get; private set; }

    // 文字在矩形里的对齐（九宫格），默认居中。矩形宽高为 0 即"以该点为锚"，如 RightCenter = 文字右端中点落在该点。
    public CanvasAlignment Alignment { get; private set; } = CanvasAlignment.Center;

    private CanvasTextStyle() { }
    CanvasTextStyle Clone() => (CanvasTextStyle)MemberwiseClone();

    // 默认样式：字号跟随宿主、居中。
    public static CanvasTextStyle Create() => new();

    public CanvasTextStyle WithFontSize(double fontSize)
    {
        if (!(fontSize > 0 && double.IsFinite(fontSize)))
            throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "Font size must be a positive finite number.");
        var c = Clone();
        c.FontSize = fontSize;
        return c;
    }

    public CanvasTextStyle WithAlignment(CanvasAlignment alignment) { var c = Clone(); c.Alignment = alignment; return c; }
}
