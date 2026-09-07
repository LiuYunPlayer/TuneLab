using System.Linq;
using System.Text.Json;
using TuneLab.Cli;
using TuneLab.Commands;
using Xunit;

namespace TuneLab.Tests;

// 命令行这一层自己的约束。命令面的判据一概不在这里测（那些在各命令的测试里）——这里只管
// "命令行有没有把话传歪"。
public class CliCommandLineTests
{
    // CLI 占掉的参数名与命令自己的参数名撞车 = 那个参数【再也传不进去】，而且用户看不出为什么
    // （--json 会被当成开关吃掉，不会报"没有这个参数"）。故盯死：注册表里任何一条命令都不许声明
    // 这几个名字。真需要时改的是 CLI 那边（换个开关名），不是让命令绕开。
    [Fact]
    public void NoCommandDeclaresAParameterTheCliHasTakenForItself()
    {
        foreach (var command in CommandRegistry.All)
        {
            using var schema = JsonDocument.Parse(command.ParametersJsonSchema);
            if (!schema.RootElement.TryGetProperty("properties", out var properties))
                continue;
            foreach (var property in properties.EnumerateObject())
                Assert.False(ProcessOptions.Names.Contains(property.Name),
                    command.Path + " declares \"" + property.Name + "\", which the CLI has taken for itself.");
        }
    }

    // ── 入口叫法：命令面的文本引用别的动作时用的是 agent 工具名，而命令行里【没有】那些名字

    // 封条：CLI 打出去的任何说明里不许留下一个 agent 工具名。照原样打出去等于让读它的人去调一条
    // 查不到、也补不全的命令。替换表由注册表给，故新加的命令自动跟上——这条测试盯的是"有没有漏"。
    [Fact]
    public void NoAgentToolNameSurvivesInWhatTheCliPrints()
    {
        var texts = CommandRegistry.All
            .SelectMany(c => new[] { c.Brief, c.Documentation, c.ParametersJsonSchema })
            // 最大的一份是脚本 API 参考（`docs script-api` 的正文），它引用工具名最多。
            .Append(TuneLab.Scripting.ScriptApiReference.Text);

        foreach (var text in texts)
        {
            var printed = CommandText.ForCli(text);
            foreach (var name in CommandRegistry.PathsByAgentToolName.Keys)
                Assert.DoesNotContain(name, printed);
        }
    }

    [Fact]
    public void ToolNamesBecomeTheCommandYouCanActuallyType()
    {
        Assert.Equal("Call tunelab setting list to see the keys.",
            CommandText.ForCli("Call list_settings to see the keys."));
        // 调用式的参数表留着（删掉会丢信息），但中间加一个空格，让它读成附注而不是函数调用。
        Assert.Equal("Change one with tunelab setting set (key, value).",
            CommandText.ForCli("Change one with set_setting(key, value)."));
        // 只换整词：碰巧含有工具名的标识符不动。
        Assert.Equal("xlist_settings", CommandText.ForCli("xlist_settings"));
    }

    [Fact]
    public void BatchLineSplitsOnWhitespace()
    {
        var tokens = TuneLab.Cli.Program.Tokenize("setting set --key AutoSaveInterval --value 20", out var error);
        Assert.Null(error);
        Assert.Equal(["setting", "set", "--key", "AutoSaveInterval", "--value", "20"], tokens);
    }

    // 值里有空格（脚本源码、带空格的路径）靠双引号成组——批量文件里最常见的一类值。
    [Fact]
    public void DoubleQuotesGroupAValueWithSpaces()
    {
        var tokens = TuneLab.Cli.Program.Tokenize("script run --code \"print('a b'); print(1)\"", out var error);
        Assert.Null(error);
        Assert.Equal(["script", "run", "--code", "print('a b'); print(1)"], tokens);
    }

    // 组内两个连写的双引号 = 一个字面双引号。这是【不用反斜杠转义】的代价，也是它的好处：
    [Fact]
    public void TwoDoubleQuotesInsideAGroupMeanOneLiteralQuote()
    {
        var tokens = TuneLab.Cli.Program.Tokenize("script run --code \"lyric = \"\"la\"\";\"", out var error);
        Assert.Null(error);
        Assert.Equal(["script", "run", "--code", "lyric = \"la\";"], tokens);
    }

    // ……反斜杠原样过：这些行里最常出现的就是 Windows 路径，反斜杠一旦是转义符就处处是坑。
    [Fact]
    public void BackslashesGoThroughUntouched()
    {
        var tokens = TuneLab.Cli.Program.Tokenize("project export --path \"C:\\Users\\me\\My Songs\\a.tlpx\"", out var error);
        Assert.Null(error);
        Assert.Equal(["project", "export", "--path", @"C:\Users\me\My Songs\a.tlpx"], tokens);
    }

    // 引号没配对就是这一行写坏了。当场报出来，好过把半截值送进命令再由它抱怨一个看不懂的参数。
    [Fact]
    public void AnUnbalancedQuoteIsReported()
    {
        TuneLab.Cli.Program.Tokenize("script run --code \"print(1)", out var error);
        Assert.NotNull(error);
    }
}
