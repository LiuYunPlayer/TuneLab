using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `setting list`（搬家前的 list_settings）的封条。
//
// Render 用合成 Data 测：一次覆盖全部分支——本地化标签与原文相同/不同、不在设置窗的条目、
// 需重启、不可由 agent 改写、带 note。真实 SettingsRegistry 里未必同时凑齐这些组合。
//
// 换行是 "\n" 而不是 Environment.NewLine——搬家前就是这样（与 project status 用 AppendLine 不同），
// 这个差别正是逐字封条要拦住的东西。
public class SettingListCommandTests
{
    static JsonObject SampleData() => new()
    {
        ["pages"] = new JsonArray
        {
            new JsonObject { ["name"] = "General", ["label"] = "常规" },
            new JsonObject { ["name"] = "Audio", ["label"] = "Audio" },   // 本地化与原名一致 → 不重复给出
        },
        ["settings"] = new JsonArray
        {
            new JsonObject
            {
                ["key"] = "MasterGain",
                ["label"] = "Master Gain",
                ["displayLabel"] = "主增益",
                ["page"] = "Audio",
                ["pageLabel"] = "音频",
                ["allowed"] = "number in [-24, 6]",
                ["current"] = -3,
                ["default"] = 0,
                ["restartRequired"] = false,
                ["agentWritable"] = true,
                ["description"] = null,
            },
            new JsonObject
            {
                ["key"] = "AgentAuthorization",
                ["label"] = "Agent Authorization",
                ["displayLabel"] = "Agent Authorization",   // 未翻译 → 不重复给出
                ["page"] = null,                            // 仅存储、不在设置窗渲染
                ["pageLabel"] = null,
                ["allowed"] = "one of Confirm, Auto",
                ["current"] = "Confirm",
                ["default"] = "Confirm",
                ["restartRequired"] = true,
                ["agentWritable"] = false,
                ["description"] = "owned by the agent sidebar",
            },
        },
    };

    [Fact]
    public void RenderKeepsTheWordingItHadBeforeTheMoveToTheCommandSurface()
    {
        var text = new SettingListCommand().Render(SampleData(), CommandArgs.Empty);

        Assert.Equal(string.Join("\n", [
            "2 application setting(s). Change one with set_setting(key, value).",
            "Settings window pages: General (\"常规\"), Audio",
            "Format: <key> \"<label>\" [page]: <allowed> — current <value>, default <value>",
            "(\"current\" is the value in the user's settings file; an empty text value means \"use the default\".)",
            "- MasterGain \"Master Gain\" (\"主增益\") [Audio (\"音频\")]: number in [-24, 6] — current -3, default 0.",
            "- AgentAuthorization \"Agent Authorization\" [not in the Settings window]: one of Confirm, Auto"
                + " — current \"Confirm\", default \"Confirm\". Needs a restart to take effect."
                + " NOT agent-writable — only the user can change it.",
            "  note: owned by the agent sidebar",
        ]), text);
    }

    // 结构化的一半：--json 与 CI 断言按这些字段名写，改了就是破坏性变更。
    // 值存【原始标量】而不是格式化文本，渲染时才化回字面量（数字不带引号、文本带引号）。
    [Fact]
    public void DataKeepsValuesAsRawScalars()
    {
        var settings = SampleData()["settings"]!.AsArray();

        // 是数字/文本本身，不是格式化后的串。断言 ValueKind 而不是 GetValue<double>()——见下一个用例。
        Assert.Equal(JsonValueKind.Number, settings[0]!["current"]!.GetValueKind());
        Assert.Equal("-3", settings[0]!["current"]!.ToJsonString());
        Assert.Equal(JsonValueKind.String, settings[1]!["current"]!.GetValueKind());

        // 渲染口径：数字裸写、文本带引号（ConfigText 的单一定义，不在 Render 里另写一套）。
        Assert.Equal("-3", ConfigText.FormatValue(settings[0]!["current"]));
        Assert.Equal("\"Confirm\"", ConfigText.FormatValue(settings[1]!["current"]));
        Assert.Equal("(none)", ConfigText.FormatValue(settings[0]!["description"]));
    }

    // JsonValue 保留装进去的那个 CLR 类型：入口给的数字可能是 int / long / double（CLI 把 flag 解析成
    // 什么就是什么，MCP 客户端也各不相同），三种都必须渲染成同一个字面量。
    // 直接 GetValue<double>() 会对 int 抛 "A value of type Int32 cannot be converted to a Double"，
    // 那是本命令搬家时踩到的真 bug，这条用例把它钉住。
    [Fact]
    public void ScalarFormattingAcceptsWhicheverNumberTypeTheCallerUsed()
    {
        Assert.Equal("-3", ConfigText.FormatValue(JsonValue.Create(-3)));
        Assert.Equal("-3", ConfigText.FormatValue(JsonValue.Create(-3L)));
        Assert.Equal("-3", ConfigText.FormatValue(JsonValue.Create(-3.0)));
        Assert.Equal("1.5", ConfigText.FormatValue(JsonValue.Create(1.5)));
        Assert.Equal("true", ConfigText.FormatValue(JsonValue.Create(true)));
    }

    // 真实注册表能被结构化枚举（字段齐全、条目数与注册表一致）。
    // 无 UI 的测试进程里 ctx.MainThread 为 null，命令就地执行——这正是那个可空调度器的用途。
    [Fact]
    public void ListsEveryRegisteredSetting()
    {
        var result = new SettingListCommand()
            .ExecuteAsync(CommandArgs.Empty, new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.False(result.IsError, result.Error?.Message);
        var settings = result.Data!["settings"]!.AsArray();
        Assert.Equal(TuneLab.Configs.SettingsRegistry.All.Count, settings.Count);
        foreach (var setting in settings)
        {
            Assert.False(string.IsNullOrEmpty(setting!["key"]!.GetValue<string>()));
            Assert.False(string.IsNullOrEmpty(setting["allowed"]!.GetValue<string>()));
        }
    }
}
