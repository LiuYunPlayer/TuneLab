using System.Collections.Generic;

namespace TuneLab.Input;

// 动作注册表：**用户在界面上够得着的每一件事**都在这里，这是「够得着」这件事的定义。
//
// 与 Keymap 是包含关系（见 EditorAction 的说明）：这里穷尽，Keymap 只引用其中值得占一个手势的那批，
// 补上手势与作用域。同一功能因此共用同一个下游 Execute——快捷键、菜单、命令面按下去的是同一件事。
//
// 静态、进程级：动作由各拥有者（Editor / PianoWindow / MainWindow / ScriptToolMenu）在建界面时注册，
// 故**没有编辑器的进程里这张表是空的**（headless / 桥未连）——命令面据此如实回答"这里做不到"，
// 而不是假装成功（判据与措辞见 ActionCommands）。
internal static class ActionRegistry
{
    static readonly Dictionary<string, EditorAction> mActions = new();
    static readonly Dictionary<string, int> mOrder = new();   // id -> 首次注册序（呈现序 = 逻辑注册序，不是字母序）
    static int mNextOrder;

    public static void Register(EditorAction action)
    {
        mActions[action.Id] = action;
        if (!mOrder.ContainsKey(action.Id))
            mOrder[action.Id] = mNextOrder++;   // 首次注册序固定，重注册（脚本随文件监视器重建）不改次序
    }

    public static void Unregister(string id) => mActions.Remove(id);

    public static IReadOnlyCollection<EditorAction> Actions => mActions.Values;

    public static bool TryGet(string id, out EditorAction action) => mActions.TryGetValue(id, out action!);

    // 首次注册序；未注册返回 int.MaxValue（排到末尾）。手势分发的同域撞键取胜也用它（注册序最小者胜，
    // 内建启动期先注册故恒胜、不被第三方脚本夺走），故两处排序共用一份序。
    public static int OrderOf(string id) => mOrder.TryGetValue(id, out var o) ? o : int.MaxValue;

    // 显示名；未注册（如脚本消失后仍留着用户绑定的孤儿 id）回落成 id 本身，好过显示空白。
    public static string LabelOf(string id) => mActions.TryGetValue(id, out var a) ? a.DisplayName() : id;

    // 触发一条动作：**先问可用性判据，不可用就不跑**并返回那句原因（null = 已执行）。
    // 键盘、菜单、命令面全走这里，故三条路径的判据是同一份（守卫不再各写一遍）。
    public static string? Execute(string id)
    {
        if (!mActions.TryGetValue(id, out var action))
            return string.Format("there is no action with id \"{0}\"", id);

        if (action.Unavailable?.Invoke() is { } reason)
            return reason;

        action.Execute();
        return null;
    }
}
