using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `source list` / `effect list`（搬家前的 list_sound_sources / list_effects）的封条。
//
// 两条命令都是分层钻取，Render 按 mode 分支：引擎清单 / 音源清单 / 参数 schema。这里用合成 Data 覆盖
// 各分支与各"不可用"路径（引擎加载不上、音源超量截断、无参数）。
// 参数 schema 那一栏在 Data 里是文本一段（schemaText），见命令类顶部对 Data 形状的说明。
public class SourceAndEffectListCommandTests
{
    static JsonArray OneEngine(string type, string displayName, string package) => new()
    {
        new JsonObject
        {
            ["type"] = type,
            ["displayName"] = displayName,
            ["activePackage"] = package,
            ["activePackageId"] = package,
            ["shadowed"] = new JsonArray(),
            ["packageDescription"] = null,
        },
    };

    // ── effect list

    [Fact]
    public void EffectEngineListPointsAtTheNextDrillDownStep()
    {
        var text = new EffectListCommand().Render(new JsonObject
        {
            ["mode"] = "engines",
            ["engines"] = OneEngine("v1.effect", "示例效果器", "示例包"),
        }, CommandArgs.Empty);

        Assert.Equal(string.Join("\n", [
            "Effect engines (1):",
            "- \"示例效果器\" [type=v1.effect, package=示例包]",
            "Pass engine=<type id> to see an engine's parameters.",
        ]), text);
    }

    // effect 全部来自插件，一个都没装时说清这一点（免得用户以为是自己没找到）。
    [Fact]
    public void EffectListExplainsThatEffectsComeFromPlugins()
    {
        Assert.Equal("No effect engines are installed. Effects come from plugins (there are no built-in effect engines).",
            new EffectListCommand().Render(new JsonObject
            {
                ["mode"] = "engines",
                ["engines"] = new JsonArray(),
            }, CommandArgs.Empty));
    }

    [Fact]
    public void EffectParametersRenderAfterTheEngineHeading()
    {
        var text = new EffectListCommand().Render(new JsonObject
        {
            ["mode"] = "parameters",
            ["engine"] = "v1.effect",
            ["displayName"] = "示例效果器",
            ["loaded"] = true,
            ["parameterCount"] = 1,
            ["schemaText"] = "\nStatic properties (1):\n- gain: number in [-24, 6]. default 0",
        }, CommandArgs.Empty);

        Assert.Equal(string.Join("\n", [
            "Effect engine \"示例效果器\" (type=v1.effect):",
            "Static properties (1):",
            "- gain: number in [-24, 6]. default 0",
        ]), text);
    }

    [Fact]
    public void EffectWithNoParametersSaysSo()
    {
        var text = new EffectListCommand().Render(new JsonObject
        {
            ["mode"] = "parameters",
            ["engine"] = "v1.effect",
            ["displayName"] = "示例效果器",
            ["loaded"] = true,
            ["parameterCount"] = 0,
            ["schemaText"] = "",
        }, CommandArgs.Empty);

        Assert.EndsWith("This effect exposes no parameters (or none at default values).", text);
    }

    // 引擎加载不上不是调用方的错：走成功路径、如实说明，而不是套上 "Error:" 前缀。
    [Fact]
    public void EffectThatFailedToLoadIsReportedAsAPlainAnswer()
    {
        Assert.Equal("The effect engine \"示例效果器\" (type=v1.effect) could not be loaded, so its parameters are unavailable.",
            new EffectListCommand().Render(new JsonObject
            {
                ["mode"] = "parameters",
                ["engine"] = "v1.effect",
                ["displayName"] = "示例效果器",
                ["loaded"] = false,
            }, CommandArgs.Empty));
    }

    // ── source list

    [Fact]
    public void SourceEngineListCoversBothKindsAndPointsAtTheNextStep()
    {
        var text = new SoundSourceListCommand().Render(new JsonObject
        {
            ["mode"] = "engines",
            ["groups"] = new JsonArray
            {
                new JsonObject { ["kind"] = "voice", ["kindLabel"] = "Voice", ["engines"] = OneEngine("v1.voice", "声库引擎", "包甲") },
                new JsonObject { ["kind"] = "instrument", ["kindLabel"] = "Instrument", ["engines"] = new JsonArray() },
            },
        }, CommandArgs.Empty);

        Assert.Equal(string.Join("\n", [
            "Voice engines (1):",
            "- \"声库引擎\" [type=v1.voice, package=包甲]",
            "Instrument engines (0):",
            "  (none)",
            "Pass engine=<type id> to list an engine's individual sources.",
        ]), text);
    }

    [Fact]
    public void SourceListShowsIdNameAndDescription()
    {
        var text = new SoundSourceListCommand().Render(new JsonObject
        {
            ["mode"] = "sources",
            ["kind"] = "voice",
            ["engine"] = "v1.voice",
            ["engineName"] = "声库引擎",
            ["loaded"] = true,
            ["total"] = 2,
            ["sources"] = new JsonArray
            {
                new JsonObject { ["id"] = "alto", ["name"] = "女中音", ["description"] = "沉稳" },
                new JsonObject { ["id"] = "tenor", ["name"] = "男高音", ["description"] = null },
            },
        }, CommandArgs.Empty);

        Assert.Equal(string.Join("\n", [
            "Sources in voice engine \"声库引擎\" (type=v1.voice), 2 source(s):",
            "- alto  \"女中音\" — 沉稳",
            "- tenor  \"男高音\"",
        ]), text);
    }

    // 超大声库：只回一部分并注明还剩多少，免得淹没调用方的上下文。
    [Fact]
    public void SourceListNotesHowManyWereLeftOut()
    {
        var sources = new JsonArray();
        for (int i = 0; i < 300; i++)
            sources.Add(new JsonObject { ["id"] = "s" + i, ["name"] = "n" + i, ["description"] = null });

        var text = new SoundSourceListCommand().Render(new JsonObject
        {
            ["mode"] = "sources",
            ["kind"] = "voice",
            ["engine"] = "v1.voice",
            ["engineName"] = "声库引擎",
            ["loaded"] = true,
            ["total"] = 312,
            ["sources"] = sources,
        }, CommandArgs.Empty);

        Assert.EndsWith("\n… (12 more; refine your request to narrow the list)", text);
    }

    [Fact]
    public void SourceParametersEndWithTheStaticReadCaveat()
    {
        var text = new SoundSourceListCommand().Render(new JsonObject
        {
            ["mode"] = "parameters",
            ["kind"] = "voice",
            ["engine"] = "v1.voice",
            ["engineName"] = "声库引擎",
            ["source"] = "alto",
            ["sourceName"] = "女中音",
            ["parameterCount"] = 1,
            ["schemaText"] = "\nPart properties (1):\n- tension: number in [0, 1]. default 0.5",
        }, CommandArgs.Empty);

        Assert.Equal(string.Join("\n", [
            "Parameters for voice source \"女中音\" (alto) in engine \"声库引擎\" (type=v1.voice):",
            "Part properties (1):",
            "- tension: number in [0, 1]. default 0.5",
            "(Schema is at default values; some engines reveal more parameters once specific values are set."
                + " Editing these is not yet scriptable.)",
        ]), text);
    }

    // ── 参数校验（不依赖任何已装引擎，故可在测试进程里真跑）

    [Fact]
    public void SourceRejectsAnUnknownKind()
    {
        var result = new SoundSourceListCommand()
            .ExecuteAsync(CommandArgs.Parse("""{"kind":"drums"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("bad_kind", result.Error!.Value.Code);
    }

    // source 属于某个 engine，单给 source 无从定位——如实报错并指出先怎么取 engine。
    [Fact]
    public void SourceWithoutEngineExplainsTheDrillDownOrder()
    {
        var result = new SoundSourceListCommand()
            .ExecuteAsync(CommandArgs.Parse("""{"source":"alto"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("source_without_engine", result.Error!.Value.Code);
        Assert.Contains("Call list_sound_sources with just engine=<type id> first", result.Error!.Value.Message);
    }
}
