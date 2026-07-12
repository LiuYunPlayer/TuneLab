using System;
using TuneLab.GUI.Input;

namespace TuneLab.Input;

// 一条可绑定命令。由拥有者（Editor/PianoWindow/MainWindow）注册，Execute 闭包捕获拥有者、直接读实时状态——
// 故不设外部注入的 KeyContext（同一键的场景自适应，如 Delete 删锚点/删音符，在 Execute 内部按实时状态分支）。
// 见 docs/keybinding-system.md §2。
internal sealed class KeyCommand
{
    // 稳定点分 id，override 存储 / 菜单绑定 / 命令面板的唯一锚点，一经发布不改。
    public required string Id { get; init; }
    // 可翻译显示名（.Tr）。走翻译、可随时改；id 不动。
    public required Func<string> DisplayName { get; init; }
    public required KeyScope Scope { get; init; }
    // 默认手势；null = 无默认（如脚本命令，由用户分配）。
    public KeyBinding? DefaultGesture { get; init; }
    public required Action Execute { get; init; }
}
