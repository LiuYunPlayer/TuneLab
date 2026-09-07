using System.Linq;
using System.Text.RegularExpressions;
using TuneLab.Commands;

namespace TuneLab.Cli;

// 命令面的文本里，引用别的动作时用的是 **agent 工具面上的名字**（"Call list_settings to see the exact
// keys"、"Change one with set_setting(key, value)"）。那些名字在命令行里【根本不存在】：照原样打出去，
// 等于让读它的人（或外部 agent）去调一条查不到、也补不全的命令，而 `tunelab --help` 里又找不着它。
//
// 故 CLI 打印任何给人看的文本之前，先按注册表那张对照表机械换成自己的叫法。
// 【只换文本】`--json` 的 Data 一个字都不动——那是机器契约，字段值不该随入口变形。
//
// 为什么不去改那 180 来处文案：它们的第一读者是模型（侧栏 agent 的工具面），那里的名字是对的；
// 一处文案同时说两套叫法反而更差。机械替换让每个入口各自看到一致的名字，且新加的命令自动跟上。
internal static class CommandText
{
    // 长名在前：短名不会先把长名的一部分吃掉（\b 已能挡住大多数，但按长度排是这类替换的常规保险）。
    static readonly Regex ToolNames = new(@"\b(" + string.Join("|", CommandRegistry.PathsByAgentToolName.Keys
        .OrderByDescending(name => name.Length).Select(Regex.Escape)) + @")\b");

    // 工具名后面常跟着调用式的参数表（"set_setting(key, value)"）。命令行的参数不是这么写的，但把参数名
    // 【删掉】会丢信息，故只在中间加一个空格，让它读成一句附注而不是一个函数调用。
    static readonly Regex CallSyntax = new(@"(tunelab [a-z-]+ [a-z-]+)\(");

    public static string ForCli(string text)
        => string.IsNullOrEmpty(text) ? text
            : CallSyntax.Replace(ToolNames.Replace(text, m => "tunelab " + CommandRegistry.PathsByAgentToolName[m.Value]), "$1 (");
}
