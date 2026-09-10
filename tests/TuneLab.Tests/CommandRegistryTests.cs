using System.Linq;
using System.Text.Json;
using TuneLab.Commands;
using Xunit;

namespace TuneLab.Tests;

// 命令注册表的自身约束。它是三个入口（agent 工具声明 / CLI 命令树与 --help / MCP tools/list）的唯一
// 真源，故这些约束破了，破的不是一处而是三处。
public class CommandRegistryTests
{
    // 模型知道的全部工具名。它们【已经写进模型的系统提示与彼此的描述文本】（"Call list_settings to see
    // the exact keys"），改名等于让模型照着不存在的名字调用。故钉成常量清单：搬家不动它，将来真要改名
    // 也得连同各处描述一起改，改到这里报错为止。（ask_user_question 不在此列：它是入口能力、不进命令树。）
    //
    // 加一条新命令时【故意】要在这里补一行——那是一次对模型可见的表面变更，该由人认领而不是自动跟上。
    // 前 25 个来自搬家（一个都不许动）；get_app_info 是搬家之后新加的第一条，动作面那三条（issue #150）随后。
    static readonly string[] AgentToolNames =
    [
        "delete_script", "export_project", "get_extension_introduction", "get_manual", "get_project_overview",
        "get_script_api", "get_script_inputs", "list_effects", "list_extension_routing", "list_extension_settings",
        "list_extensions", "list_keybindings", "list_scripts", "list_settings", "list_sound_sources",
        "read_script", "run_in_sandbox", "run_saved_script", "run_script", "save_script",
        "set_extension_enabled", "set_extension_routing", "set_extension_setting", "set_keybinding", "set_setting",
        "get_app_info", "list_actions", "run_action", "get_editor_status",
        // issue #150 的后续项：清单上"外部还够不着"的那几个桶各自补的命令。
        "open_project", "install_extension", "uninstall_extension", "cancel_extension_uninstall",
        // part preset 的外部面（覆盖率清单最后一个桶）。
        "list_presets", "apply_preset", "save_preset", "delete_preset", "rename_preset",
        // 保存那一族：走用户口径的保存（改保存路径、清未保存态），与只写副本的 export_project 分开。
        "save_project", "save_project_as",
        // 渲染音频：与 export_project 分开（那条写的是工程文件），且它是唯一一条要先等合成跑完的。
        "export_audio",
    ];

    [Fact]
    public void EveryToolNameTheModelKnowsStillResolvesToACommand()
    {
        Assert.Equal(AgentToolNames.OrderBy(n => n), CommandRegistry.All.Select(c => c.AgentToolName).OrderBy(n => n));
    }

    // 不需要宿主 = 答案只来自程序自带的东西（编译进来的常量、随包的资源），故各入口都能就地答。
    // 那就必须是【只读】的：会写的命令要过宿主的闸门与授权策略，就地答等于把这两样绕过去。
    [Fact]
    public void OnlyReadCommandsCanAnswerWithoutAHost()
    {
        foreach (var command in CommandRegistry.All)
        {
            if (!command.NeedsHost)
                Assert.Equal(CommandKind.Read, command.Kind);
        }
    }

    [Fact]
    public void PathsAndToolNamesAreUnique()
    {
        Assert.Equal(CommandRegistry.All.Count, CommandRegistry.All.Select(c => c.Path).Distinct().Count());
        Assert.Equal(CommandRegistry.All.Count, CommandRegistry.All.Select(c => c.AgentToolName).Distinct().Count());
    }

    // 路径恒为 "<group> <verb>"：CLI 的子命令与 MCP 的 group × {read, edit} 合成都按它切分。
    [Fact]
    public void EveryPathIsAGroupAndAVerb()
    {
        foreach (var command in CommandRegistry.All)
        {
            var parts = command.Path.Split(' ');
            Assert.True(parts.Length == 2, command.Path);
            Assert.All(parts, p => Assert.False(string.IsNullOrWhiteSpace(p), command.Path));
        }
    }

    [Fact]
    public void EveryCommandCanBeLookedUpByItsPath()
    {
        foreach (var command in CommandRegistry.All)
        {
            Assert.True(CommandRegistry.TryGet(command.Path, out var found));
            Assert.Same(command, found);
        }
        Assert.False(CommandRegistry.TryGet("no such command", out _));
    }

    // Brief 进 CLI 的一行帮助，长了会把那张表撑散。
    [Fact]
    public void BriefIsAOneLinerAndDocumentationIsNotEmpty()
    {
        foreach (var command in CommandRegistry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Brief), command.Path);
            Assert.True(command.Brief.Length <= 60, command.Path + ": " + command.Brief.Length + " chars");
            Assert.DoesNotContain('\n', command.Brief);
            Assert.False(string.IsNullOrWhiteSpace(command.Documentation), command.Path);
        }
    }

    // 参数 schema 原样进 agent 的工具声明与 MCP 的 inputSchema，坏 JSON 会让整个工具面报废。
    [Fact]
    public void EveryParameterSchemaIsAJsonObject()
    {
        foreach (var command in CommandRegistry.All)
        {
            using var doc = JsonDocument.Parse(command.ParametersJsonSchema);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
        }
    }
}
