using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `script list` / `script read` / `script inputs`（搬家前的 list_scripts / read_script /
// get_script_inputs）的封条。渲染用合成 Data 测——不依赖这台机器的脚本库里有什么。
//
// 两处特别不能"顺手改好看"：
//  · read 的回灌是【源码原样】，不带任何抬头——多一行抬头就会被模型连着抄进改写后的代码里；
//  · 找不到脚本那句在读与写两边必须一字不差（调用方据它回列表核名字），故收在 SavedScriptSupport。
public class ScriptCommandsTests
{
    // ── script list

    [Fact]
    public void EmptyLibrarySaysSo()
    {
        Assert.Equal("The script library is empty.",
            new ScriptListCommand().Render(new JsonObject { ["scripts"] = new JsonArray() }, CommandArgs.Empty));
    }

    [Fact]
    public void MenuToolsAndPlainScriptsAreMarkedApart()
    {
        var text = new ScriptListCommand().Render(new JsonObject
        {
            ["scripts"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "fade-notes",
                    ["tool"] = new JsonObject
                    {
                        ["displayName"] = "渐弱",
                        ["context"] = "note",
                        ["hasInputs"] = true,
                    },
                },
                new JsonObject { ["name"] = "scratch", ["tool"] = null },
            },
        }, CommandArgs.Empty);

        Assert.StartsWith("2 script(s):", text);
        Assert.Contains("\n- fade-notes  [tool \"渐弱\", context=note] (takes inputs)", text);
        Assert.Contains("\n- scratch  [plain]", text);
    }

    // 工具脚本不带入参时不该冒出 (takes inputs)——调用方会据它决定要不要先读 schema。
    [Fact]
    public void ToolWithoutInputsIsNotFlagged()
    {
        var text = new ScriptListCommand().Render(new JsonObject
        {
            ["scripts"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "tidy",
                    ["tool"] = new JsonObject { ["displayName"] = "整理", ["context"] = "part", ["hasInputs"] = false },
                },
            },
        }, CommandArgs.Empty);

        Assert.Contains("[tool \"整理\", context=part]", text);
        Assert.DoesNotContain("takes inputs", text);
    }

    // ── script read

    [Fact]
    public void ReadReturnsTheSourceVerbatimWithoutAnyHeading()
    {
        const string code = "function main() {\n  tl.log('hi')\n}\n";

        Assert.Equal(code, new ScriptReadCommand().Render(
            new JsonObject { ["name"] = "hi", ["code"] = code }, CommandArgs.Empty));
    }

    [Fact]
    public void ReadingAnUnknownScriptPointsAtTheList()
    {
        var result = new ScriptReadCommand()
            .ExecuteAsync(CommandArgs.Parse("""{"name":"no-such-script-xyz"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("not_found", result.Error!.Value.Code);
        // "Error: " 前缀由入口加，故 message 自身不带。
        Assert.Equal("no script named \"no-such-script-xyz\". Call list_scripts to see available names.", result.Error!.Value.Message);
    }

    // ── script inputs

    [Fact]
    public void ScriptWithoutInputsSaysToJustRunIt()
    {
        Assert.Equal("Script \"tidy\" takes no inputs. Run it with run_saved_script and no `inputs`.",
            new ScriptInputsCommand().Render(new JsonObject { ["name"] = "tidy", ["hasInputs"] = false }, CommandArgs.Empty));
    }

    // 入参 schema 是一段文本（判据：产物的事实形态就是一份参数说明，格式化早已收口在 ConfigText），
    // 渲染原样给出、不再包装。
    [Fact]
    public void InputSchemaTextIsPassedThroughUntouched()
    {
        const string schema = "Inputs for script \"fade\" (1 field(s)). …\n- amount: number in [0, 1]. default 0.5";

        Assert.Equal(schema, new ScriptInputsCommand().Render(
            new JsonObject { ["name"] = "fade", ["hasInputs"] = true, ["schemaText"] = schema }, CommandArgs.Empty));
    }

    [Fact]
    public void InputsOfAnUnknownScriptFailBeforeTouchingTheProject()
    {
        // 名字先于工程检查——库里根本没有这个脚本时，"没开工程"不是调用方该收到的答复。
        var result = new ScriptInputsCommand()
            .ExecuteAsync(CommandArgs.Parse("""{"name":"no-such-script-xyz"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("not_found", result.Error!.Value.Code);
    }
}
