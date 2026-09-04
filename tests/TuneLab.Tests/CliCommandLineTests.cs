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
