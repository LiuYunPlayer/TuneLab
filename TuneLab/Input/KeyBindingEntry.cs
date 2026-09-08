using TuneLab.GUI.Input;

namespace TuneLab.Input;

// keymap 里的一条：**引用**一条动作（ActionRegistry 里的 id），再补上手势与分发作用域。
//
// 有这条 = 那条动作「可绑」（设置页只列这些，UX 与从前一样）——故不另设 Bindable 布尔：
// 「值不值得占一个手势」这层筛选就体现在**有没有 keymap 条目**上，一份数据一处真源。
// 动作本身（显示名、后果分档、能不能跑、怎么跑）一律去 ActionRegistry 问，这里不复制任何一份。
internal sealed class KeyBindingEntry
{
    public required string ActionId { get; init; }
    public required KeyScope Scope { get; init; }
    // 默认手势；null = 无默认（如未声明手势的脚本动作，由用户分配）。
    public KeyBinding? DefaultGesture { get; init; }

    public string DisplayName() => ActionRegistry.LabelOf(ActionId);
}
