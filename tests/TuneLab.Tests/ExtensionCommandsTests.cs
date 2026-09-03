using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `extension routing` / `extension introduction`（搬家前的 list_extension_routing /
// get_extension_introduction）的封条。
//
// 这两条的措辞里有两处特别不能"顺手统一"掉：
//  · 没有路由冲突时要把排查【引向别处】而不是只说"没冲突"——否则排障就断在这儿了；
//  · 能力位没有作者自述时给包级描述，必须**标注它是降级参考**并明说别转述给用户——包的自述讲的是
//    整个包，可能涵盖别的能力。
public class ExtensionCommandsTests
{
    // ── extension routing

    [Fact]
    public void NoConflictAnswerPointsTheTroubleshootingElsewhere()
    {
        var text = new ExtensionRoutingCommand().Render(
            new JsonObject { ["conflicts"] = new JsonArray() }, CommandArgs.Empty);

        Assert.StartsWith("No routing conflicts: every extension identity (engine id / file format) is provided by exactly one installed package", text);
        Assert.Contains("If a plugin still isn't working, look elsewhere", text);
        Assert.Contains("list_sound_sources / list_effects", text);
    }

    [Fact]
    public void ConflictListsEveryCandidateAndMarksTheActiveOne()
    {
        var text = new ExtensionRoutingCommand().Render(new JsonObject
        {
            ["conflicts"] = new JsonArray
            {
                new JsonObject
                {
                    ["kind"] = "voice",
                    ["identity"] = "v1.voice",
                    ["activePackageId"] = "pkg.a",
                    ["activePackage"] = "包甲",
                    ["chosenByUser"] = true,
                    ["options"] = new JsonArray
                    {
                        new JsonObject { ["packageId"] = "pkg.a", ["package"] = "包甲", ["active"] = true },
                        new JsonObject { ["packageId"] = "pkg.b", ["package"] = "包乙", ["active"] = false },
                    },
                },
            },
        }, CommandArgs.Empty);

        Assert.Contains("1 contested identity(ies)", text);
        Assert.Contains("- voice:v1.voice  active = \"包甲\" (packageId pkg.a, chosen by the user)", text);
        Assert.Contains("\n    · ACTIVE   \"包甲\" [packageId=pkg.a]", text);
        Assert.Contains("\n    · shadowed \"包乙\" [packageId=pkg.b]", text);
        Assert.EndsWith("(kind = voice / instrument / effect / format-import / format-export.)", text);
    }

    // 用户没选过时说明默认规则是什么（他才知道要不要显式选）。
    [Fact]
    public void ConflictSaysWhenTheWinnerCameFromTheDefaultRule()
    {
        var text = new ExtensionRoutingCommand().Render(new JsonObject
        {
            ["conflicts"] = new JsonArray
            {
                new JsonObject
                {
                    ["kind"] = "effect",
                    ["identity"] = "v1.effect",
                    ["activePackageId"] = "pkg.a",
                    ["activePackage"] = "包甲",
                    ["chosenByUser"] = false,
                    ["options"] = new JsonArray
                    {
                        new JsonObject { ["packageId"] = "pkg.a", ["package"] = "包甲", ["active"] = true },
                    },
                },
            },
        }, CommandArgs.Empty);

        Assert.Contains(", by the default rule)", text);
        Assert.Contains("the built-in implementation wins, otherwise the package whose id sorts first", text);
    }

    // ── extension introduction

    static JsonObject Capability(string kind, string identity, string displayName, string package) => new()
    {
        ["kind"] = kind,
        ["identities"] = new JsonArray { identity },
        ["displayName"] = displayName,
        ["package"] = package,
    };

    [Fact]
    public void IntroductionIsAttributedToThePluginAuthor()
    {
        var data = Capability("voice", "v1.voice", "示例引擎", "示例包");
        data["hasIntroduction"] = true;
        data["omittedChars"] = 0;
        data["text"] = "# 用法\n随便写";

        Assert.Equal("Introduction for voice:v1.voice (\"示例引擎\", from package \"示例包\"),"
            + " as written by the plugin author:\n\n# 用法\n随便写",
            new ExtensionIntroductionCommand().Render(data, CommandArgs.Empty));
    }

    [Fact]
    public void LongIntroductionSaysHowMuchWasCutOff()
    {
        var data = Capability("voice", "v1.voice", "示例引擎", "示例包");
        data["hasIntroduction"] = true;
        data["omittedChars"] = 1234;
        data["text"] = "开头";

        Assert.EndsWith("开头\n\n… (introduction truncated; 1234 more characters)",
            new ExtensionIntroductionCommand().Render(data, CommandArgs.Empty));
    }

    // 关键：包级描述是降级参考，必须标注清楚并明说别转述给用户。
    [Fact]
    public void MissingIntroductionFallsBackToThePackageBlurbWithAWarning()
    {
        var data = Capability("effect", "v1.effect", "示例效果器", "示例包");
        data["hasIntroduction"] = false;
        data["packageDescription"] = "一套示例插件";

        var text = new ExtensionIntroductionCommand().Render(data, CommandArgs.Empty);

        Assert.Contains("ships no introduction — its author wrote none.", text);
        Assert.Contains("Falling back to the PACKAGE-level description, given only because this capability has none of its own:", text);
        Assert.Contains("一套示例插件", text);
        Assert.Contains("Do not relay it to the user as what this capability does.", text);
    }

    [Fact]
    public void MissingIntroductionAndNoBlurbSaysNothingIsKnown()
    {
        var data = Capability("format-import", "mid", "MIDI", "示例包");
        data["hasIntroduction"] = false;
        data["packageDescription"] = null;

        Assert.EndsWith("Nothing more is known about this capability than its name.",
            new ExtensionIntroductionCommand().Render(data, CommandArgs.Empty));
    }

    [Fact]
    public void EmptyCapabilityIsRejected()
    {
        var result = new ExtensionIntroductionCommand()
            .ExecuteAsync(CommandArgs.Parse("""{"capability":"  "}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("empty_capability", result.Error!.Value.Code);
    }

    // 查不到的能力位：报错并指路（"Error:" 前缀由入口加，故 message 自身不带）。
    [Fact]
    public void UnknownCapabilityPointsAtTheExtensionList()
    {
        var result = new ExtensionIntroductionCommand()
            .ExecuteAsync(CommandArgs.Parse("""{"capability":"nope.nothing"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("not_found", result.Error!.Value.Code);
        Assert.StartsWith("no installed capability matches", result.Error!.Value.Message);
        Assert.Contains("Call list_extensions", result.Error!.Value.Message);
    }
}
