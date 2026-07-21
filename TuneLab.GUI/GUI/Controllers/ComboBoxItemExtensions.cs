using TuneLab.SDK;

namespace TuneLab.GUI.Controllers;

// ComboBoxItem 的宿主侧呈现投影：SDK 面只承载数据（Value / DisplayText / SubItems），
// 「显示成什么文本」是渲染器（本 GUI）的私事，故做成宿主扩展而非 SDK 实例成员。
internal static class ComboBoxItemExtensions
{
    // 显示文本：优先 DisplayText，缺省回退到值的字面量。
    public static string ShowText(this ComboBoxItem item) => item.DisplayText ?? item.Value.ToString() ?? string.Empty;
}
