namespace TuneLab.Input;

// 快捷键作用域。分发时每个能收键的控件只用自身 scope 调 Keymap.TryHandle；
// 内层控件（PianoWindow）借 Avalonia 事件冒泡先收到，未命中自然冒泡到外层（Editor→Global）——
// 内层优先由冒泡顺序天然给出，无需中央栈。见 docs/keybinding-system.md §3。
internal enum KeyScope
{
    Global,
    Editor,
    TrackWindow,
    PianoWindow,
}
