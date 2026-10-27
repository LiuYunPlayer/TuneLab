namespace TuneLab.SDK;

// 弹出文本框（ICanvasHost.PopupTextBox）的选项。不可变，构造函数全封，只走静态工厂 + With——以后加占位提示、长度上限等
// 是加一个 With，不动 PopupTextBox 的签名。可以建好存进静态字段反复用。
public sealed class CanvasTextBoxOptions
{
    // 多行：可以输入换行（Shift+Enter 换行，Enter 仍是确认；文字里的换行是 \n）。与 TextBoxConfig.WithMultiline 同名同义；
    // 这里不带"最多显示几行"——弹出框的大小就是给定的矩形，不随内容变高。
    public bool IsMultiline { get; private set; }

    private CanvasTextBoxOptions() { }
    CanvasTextBoxOptions Clone() => (CanvasTextBoxOptions)MemberwiseClone();

    public static CanvasTextBoxOptions Create() => new();

    public CanvasTextBoxOptions WithMultiline(bool value = true) { var c = Clone(); c.IsMultiline = value; return c; }
}
