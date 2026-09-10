using System.Collections.Generic;
using System.Diagnostics;

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
        // 执行路径：无参动作只有 Execute；带**必填**选择器参数的只有 Parameter.Execute（两条都填/都不填
        // 都是注册方写错了，而那种错在运行期表现为"回报已执行、实际什么都没发生"——正是这个面最该避免的）；
        // 带**可选**修饰符参数的两条都要有——给值走 Parameter.Execute，不给走 Execute 那条缺省路径。
        if (action.Parameter is { Optional: true } optional)
        {
            Debug.Assert(action.Execute != null,
                "An action with an optional parameter needs Execute as its default path (id: " + action.Id + ")");
            Debug.Assert(optional.DefaultBehavior != null,
                "An optional parameter must say what happens without a value (DefaultBehavior, id: " + action.Id + ")");
        }
        else
        {
            Debug.Assert((action.Execute == null) != (action.Parameter == null),
                "An action must have exactly one execution path: Execute, or Parameter.Execute (id: " + action.Id + ")");
        }
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
    // argument = 带【选择器参数】的动作要选的那个成员（见 ActionParameter），无参动作留空。
    // 默认值让界面入口与键盘分发保持原样调用：它们从不给参数——必填参数的动作不进 Keymap、界面入口
    // 自己就在那个成员上；**可选**参数的动作按下手势就是要那条缺省行为。
    public static string? Execute(string id, string argument = "")
    {
        if (!mActions.TryGetValue(id, out var action))
            return string.Format("there is no action with id \"{0}\"", id);

        if (action.Unavailable?.Invoke() is { } reason)
            return reason;

        if (action.Parameter is { } parameter)
        {
            if (argument.Length != 0)
            {
                // 值域即判据（见 ActionParameter）：落在集合外就不跑，并把此刻的合法值说出来。
                if (!parameter.TryResolve(argument, out var value, out var error))
                    return error;

                parameter.Execute(value.Value);
                return null;
            }

            // 不给值：可选参数落到下面那条缺省路径，必填的则明说缺的是什么。
            if (!parameter.Optional)
                return string.Format("it needs a value for \"{0}\" — the thing to act on. Valid right now: {1}", parameter.Name, ActionParameter.Describe(parameter.Values()));
        }
        else if (argument.Length != 0)
        {
            return string.Format("it takes no argument, but \"{0}\" was given", argument);
        }

        // Execute 与 Parameter 恰有其一（注册时已校验）；仍不空跑一趟，免得写错的注册变成假装成功。
        if (action.Execute == null)
            return "it has no execution path registered (this is a bug in TuneLab)";

        action.Execute();
        return null;
    }
}
