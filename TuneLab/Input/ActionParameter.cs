using System;
using System.Collections.Generic;
using System.Text;

namespace TuneLab.Input;

// 一条动作的【选择器参数】：动作面上唯一的带参形状——一个动词 + 一个闭集里选出来的成员
//（`parameter.showSynthesizedTrack` × 某条合成参数轨）。
//
// 【为什么要它，以及为什么只有这一种形状】动作面是穷尽面，而界面上有一批入口的成员随工程变
// （参数面板的合成参数轨随 part 的声源与效果器链变、参数栏钉选随属性声明变）：逐成员开 id 会爆，
// 且那些 id 明天就不存在了，"一经发布不改"根本无从保证。反过来，成员**固定且有限**的
//（5 支笔、6 个侧栏面、18 档量化）仍逐值开一条终态动作，不回收成带参的一条——理由见
// docs/command-surface.md §5.5 的那条口径。故判据是：加一个成员是加一个**值**，不是加一条动作。
//
// 【值域必须能自省】动态成员集意味着调用方无法预先知道合法值。故 Values 是闭包而非常量表，
// `action list` 把"这条动作接什么参数、此刻有哪些合法值"一起报出去（同 `setting list` 报 allowed values
// 的理由）——否则外部只能猜，而猜错在这个面上表现为静默什么都没发生。
//
// 【为什么没有逐值的可用性判据】值域本身就是"此刻的合法值"，落在集合外的由 TryResolve 拒掉并列出合法值；
// "整条动作现在跑不动"（没开 part、声明面是空的）仍由 EditorAction.Unavailable 说。真出现"值在集合里、
// 但这一个值此刻还是不能跑"的动作时，再加一个接值的判据委托即可（加性，既有动作不受影响）——
// 今天没有这样的动作，先不造那一层。
//
// 【为什么只有一个参数】今天的形状是"一个动词 + 一个选择器"，命令面因此只收一个 `argument` 字符串。
// 真需要第二个参数时加一个 `arguments` 对象（加性），不必回头改这一批。
internal sealed class ActionParameter
{
    // 参数名（进 `action run` 的回报与参数说明，如 "track" / "property"）。稳定、裸名。
    public required string Name { get; init; }

    // 一句英文说清这个参数选的是什么（原样进 `action list` 的回报）。
    public required string Description { get; init; }

    // 此刻的合法值，按界面上的呈现序。**动态集**：成员随工程/界面状态变，故每次现算。
    public required Func<IReadOnlyList<ActionArgument>> Values { get; init; }

    // 带参动作的执行：拿到的是**已经过 TryResolve 校验**的 Value（不是外部原样给的串）。
    public required Action<string> Execute { get; init; }

    // 把外部给的一个串落到一个合法值上。认 Value（大小写不敏感）与 Label（同）——一处认一种写法、
    // 另一处不认，调用方就会在两边来回试错（同 ExtensionCapabilityLookup 认三种写法的理由）。
    // 失败时 error 是"它不是合法值"外加**此刻的合法值**：那是调用方唯一能自救的信息。
    public bool TryResolve(string argument, out ActionArgument value, out string error)
    {
        var values = Values();
        foreach (var candidate in values)
        {
            if (string.Equals(candidate.Value, argument, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Label, argument, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                error = string.Empty;
                return true;
            }
        }

        value = default;
        error = values.Count == 0
            ? string.Format("there is nothing to pick for \"{0}\" right now", Name)
            : string.Format("\"{0}\" is not one of the values for \"{1}\". Valid right now: {2}", argument, Name, Describe(values));
        return false;
    }

    // 合法值的一行摘要（回报与错误消息共用）。过多则截断并如实说是前几个（同 setting 的选项截断口径）。
    public static string Describe(IReadOnlyList<ActionArgument> values)
    {
        var sb = new StringBuilder();
        int listed = Math.Min(values.Count, MaxListedValues);
        for (int i = 0; i < listed; i++)
        {
            if (i > 0)
                sb.Append(", ");
            values[i].Append(sb);
        }
        if (values.Count > listed)
            sb.Append(string.Format(" (first {0} of {1}; pass any valid one)", listed, values.Count));
        return sb.ToString();
    }

    const int MaxListedValues = 12;   // 同 SettingsText 的选项截断口径：值太多时不淹没上下文
}

// 一个合法值：Value 是机器用的稳定 token（原样进 `action run --argument`），Label 是人看的名字
// （界面上那一条的措辞），State 是这个值**此刻的相关状态**（如 "shown" / "hidden" / "pinned"）——
// 只是自省帮助、不是契约：终态动作幂等，调用方本不必先读一次才敢按（见 §5.5 的"终态优先"）。
internal readonly record struct ActionArgument(string Value, string Label, string? State = null)
{
    public void Append(StringBuilder sb)
    {
        sb.Append(Value);
        if (Label.Length != 0 && Label != Value)
            sb.Append(" \"").Append(Label).Append('"');
        if (!string.IsNullOrEmpty(State))
            sb.Append(" (").Append(State).Append(')');
    }
}
