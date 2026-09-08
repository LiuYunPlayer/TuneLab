using Avalonia.Controls;
using Avalonia.Input;
using TuneLab.GUI.Input;
using KeyBinding = TuneLab.GUI.Input.KeyBinding;   // 消歧：Avalonia.Input 也有 KeyBinding

namespace TuneLab.Input;

internal static class KeymapMenuExtensions
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

    static KeyGesture? ToKeyGesture(KeyBinding? binding)
        => binding == null ? null : new KeyGesture(binding.Value.Key, binding.Value.Modifiers);
}
