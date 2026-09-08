using Avalonia.Controls;
using Avalonia.Input;
using TuneLab.GUI.Input;
using KeyBinding = TuneLab.GUI.Input.KeyBinding;   // 消歧：Avalonia.Input 也有 KeyBinding

namespace TuneLab.Input;

// 把界面上的一个入口（菜单项 / 按钮）绑到一条【动作 id】上。
//
// 【为什么值得一个扩展方法】动作注册表是"够得着"的定义，那么反过来说：一个按钮的处理器里写着内联逻辑、
// 没有对应动作，按定义就是"外部够不着的地方"。让入口只说自己触发哪条动作，三件事同时成立——
//  · 点击与快捷键、命令面按下去的是同一件事（同一个 Execute，判据也同一份）；
//  · 可用性判据只写在 EditorAction.Unavailable 一处，入口不必各自守卫；
//  · 覆盖率扫描能【机械地】看出这个入口有没有对应动作（见 docs/action-coverage.md 与 ActionCoverageTests）。
internal static class ActionBindings
{
    // 菜单项绑定到动作 id：显示手势实时取 Keymap.Effective（随 Keymap.Changed 刷新），点击触发那条动作。
    // 取代 SetShortcut/SetInputGesture 的手势硬编码——菜单显示与键盘分发共用 Keymap 单一真相源，二者不再漂移。
    // 前提：该动作须已注册（Editor 内 RegisterActions 先于 CreateMenu 调用）。
    public static MenuItem SetAction(this MenuItem item, string id)
    {
        item.Command = ReactiveUI.ReactiveCommand.Create(() => ActionRegistry.Execute(id));
        void Refresh() => item.InputGesture = ToKeyGesture(Keymap.Effective(id));
        Refresh();
        // 菜单项与 Editor（单例）同生命周期，无需解绑。
        Keymap.Changed += Refresh;
        return item;
    }

    // 按钮绑定到动作 id。不显示手势（按钮上没有摆它的位置），故与菜单项不同，**注册先后无所谓**：
    // 这里只记下 id，判据与执行都推迟到点下去那一刻。
    public static T SetAction<T>(this T button, string id) where T : GUI.Components.Button
    {
        button.Clicked += () => ActionRegistry.Execute(id);
        return button;
    }

    static KeyGesture? ToKeyGesture(KeyBinding? binding)
        => binding == null ? null : new KeyGesture(binding.Value.Key, binding.Value.Modifiers);
}
