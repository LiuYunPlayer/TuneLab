using TuneLab.Foundation;

namespace TuneLab.SDK;

// 原 StringConfig（按 UI 控件命名）。构造函数全封，只走静态工厂 + With。
// With 走 Clone（与 SliderConfig 同款）：加字段只改声明处，不逐个 With 手抄字段。
public sealed class TextBoxConfig : IValueConfig<string>
{
    public string DefaultValue { get; private set; } = "";

    // 掩码显示（如 API Key 等敏感字段）。仅影响显示，不改变存值类型，仍是普通 string property。
    public bool IsPassword { get; private set; }

    // 多行输入（如提示词、说明文本、逐行清单）。值仍是普通 string property，换行以 \n 存在其中——
    // 不引入新的值形态，故序列化 / 撤销 / 多选扇出全走既有那条路。与 IsPassword 互斥（掩码只有单行有）。
    public bool IsMultiline { get; private set; }

    // 多行时最多显示几行：框贴着内容长（一行内容就一行高），到这个行数封顶、其后框内滚动。
    // 【是上限不是定高】——定高会让只有一行的值白占好几行。**0 = 不封顶**（框随内容一直长高，与宿主的
    // 「0 = 不限」惯例一致）。仅 IsMultiline 时有意义；单行忽略。
    // 给行数而不是像素：像素要插件去猜宿主的字号与内边距，而行数是插件真正知道的那件事（"这字段大概几行"）。
    public int MaxVisibleLines { get; private set; }

    private TextBoxConfig() { }
    TextBoxConfig Clone() => (TextBoxConfig)MemberwiseClone();

    public static TextBoxConfig Create(string defaultValue = "") => new() { DefaultValue = defaultValue };

    public TextBoxConfig WithPassword(bool value = true) { var c = Clone(); c.IsPassword = value; return c; }

    // 多行输入，maxVisibleLines = 最多显示几行：框贴着内容长、到此封顶、其后框内滚动；0 = 不封顶。
    // 负数按 0（不封顶）收——一个装饰性参数不值得让插件在加载期抛异常。
    public TextBoxConfig WithMultiline(int maxVisibleLines = 0) { var c = Clone(); c.IsMultiline = true; c.MaxVisibleLines = maxVisibleLines < 0 ? 0 : maxVisibleLines; return c; }

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
