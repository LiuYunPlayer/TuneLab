using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `extension settings`（搬家前的 list_extension_settings）的封条。渲染用合成 Data 测——一次覆盖全分支，
// 且不必真装一个声明了设置的插件。
//
// 这里最要紧的一条是【密钥政策】：声明为 IsPassword 的字段只报有没有设过，值绝不出现在回报里。
// 故 SecretValueIsNeverRendered 特意在 Data 里塞一个明文，断言渲染出来的文本里【找不到它】——
// 政策要落在渲染这一步上，不能只靠"取事实那步不会放进来"这一个约定。
public class ExtensionSettingsCommandTests
{
    static readonly ExtensionSettingsCommand Command = new();

    static string Render(JsonObject data) => Command.Render(data, CommandArgs.Empty);

    // ── 无参模式：有哪些扩展声明了设置

    [Fact]
    public void NoExtensionWithSettingsSaysSoAndSeparatesItFromPluginParameters()
    {
        var text = Render(new JsonObject { ["extensions"] = new JsonArray() });

        Assert.Equal("No installed extension declares its own settings. "
            + "(Plugin parameters that belong to a part/note are a different thing — see list_sound_sources / list_effects.)", text);
    }

    [Fact]
    public void ExtensionListGivesTheKeyPackageAndFieldCount()
    {
        var text = Render(new JsonObject
        {
            ["extensions"] = new JsonArray
            {
                new JsonObject
                {
                    ["extensionKey"] = "voice:v1.voice",
                    ["displayName"] = "示例引擎",
                    ["package"] = "示例包",
                    ["packageId"] = "pkg.a",
                    ["fieldCount"] = 3,
                    ["schemaError"] = null,
                },
            },
        });

        Assert.StartsWith("1 extension(s) with their own settings:", text);
        Assert.Contains("\n- voice:v1.voice \"示例引擎\" [package=示例包, packageId=pkg.a]  3 field(s)", text);
        Assert.EndsWith("\nPass extension=<id or name> to see one extension's fields. The user edits these in the Settings window's Extensions page.", text);
    }

    // 某个扩展声明设置时抛错只是清单里的一格局部事实：照常列出它，标注失败原因，其余条目不受影响。
    [Fact]
    public void OneExtensionFailingToDeclareItsSettingsIsAnnotatedInPlace()
    {
        var text = Render(new JsonObject
        {
            ["extensions"] = new JsonArray
            {
                new JsonObject
                {
                    ["extensionKey"] = "effect:v1.effect",
                    ["displayName"] = "坏的",
                    ["package"] = "示例包",
                    ["packageId"] = "pkg.a",
                    ["fieldCount"] = null,
                    ["schemaError"] = "boom",
                },
                new JsonObject
                {
                    ["extensionKey"] = "voice:v1.voice",
                    ["displayName"] = "好的",
                    ["package"] = "示例包",
                    ["packageId"] = "pkg.a",
                    ["fieldCount"] = 1,
                    ["schemaError"] = null,
                },
            },
        });

        Assert.Contains("2 extension(s) with their own settings:", text);
        Assert.Contains("[package=示例包, packageId=pkg.a]  (the extension failed to declare its settings — boom)", text);
        Assert.Contains("\"好的\" [package=示例包, packageId=pkg.a]  1 field(s)", text);
    }

    // ── 单个扩展模式：它有哪些字段

    static JsonObject Extension(JsonArray fields) => new()
    {
        ["extensionKey"] = "voice:v1.voice",
        ["displayName"] = "示例引擎",
        ["package"] = "示例包",
        ["packageId"] = "pkg.a",
        ["fields"] = fields,
    };

    [Fact]
    public void FieldsGiveAllowedCurrentAndDefault()
    {
        var text = Render(Extension(new JsonArray
        {
            new JsonObject
            {
                ["key"] = "modelPath",
                ["label"] = "Model path",
                ["secret"] = false,
                ["allowed"] = "text",
                ["hasCurrent"] = true,
                ["current"] = "D:/m.onnx",
                ["hasDefault"] = true,
                ["default"] = "",
            },
        }));

        Assert.StartsWith("Settings of \"示例引擎\" (voice:v1.voice, package \"示例包\"), 1 field(s). Change one with set_extension_setting."
            + "\nFormat: <key> \"<label>\": <allowed> — current <value>, default <value>", text);
        Assert.Contains("\n- modelPath (\"Model path\"): text — current \"D:/m.onnx\", default \"\"", text);
        Assert.EndsWith("\n(Fields can appear/disappear depending on other values — re-list after a change if you expect that.)", text);
    }

    // 没存过值 ≠ 存了个空值：前者要说明"默认生效"，好让调用方知道改它才会写进文件。
    [Fact]
    public void UnsetFieldSaysTheDefaultApplies()
    {
        var text = Render(Extension(new JsonArray
        {
            new JsonObject
            {
                ["key"] = "device",
                ["secret"] = false,
                ["allowed"] = "one of [cpu, cuda]",
                ["hasCurrent"] = false,
                ["current"] = null,
                ["hasDefault"] = true,
                ["default"] = "cpu",
            },
        }));

        Assert.Contains("\n- device: one of [cpu, cuda] — current (unset, so the default applies), default \"cpu\"", text);
    }

    // 分组/复合字段没有默认值可报（IValueConfig 才有），此时不能凭空写一个 "(none)" 出去。
    [Fact]
    public void FieldWithoutADefaultOmitsThatHalf()
    {
        var text = Render(Extension(new JsonArray
        {
            new JsonObject
            {
                ["key"] = "advanced",
                ["secret"] = false,
                ["allowed"] = "a group of fields",
                ["hasCurrent"] = false,
                ["current"] = null,
                ["hasDefault"] = false,
                ["default"] = null,
            },
        }));

        Assert.Contains("\n- advanced: a group of fields — current (unset, so the default applies)\n", text);
        Assert.DoesNotContain("default (none)", text);
    }

    [Fact]
    public void SecretFieldOnlyReportsWhetherItIsSet()
    {
        var set = Render(Extension(new JsonArray
        {
            new JsonObject { ["key"] = "apiKey", ["secret"] = true, ["secretSet"] = true },
        }));
        var notSet = Render(Extension(new JsonArray
        {
            new JsonObject { ["key"] = "apiKey", ["secret"] = true, ["secretSet"] = false },
        }));

        Assert.Contains("\n- apiKey: secret text — currently SET (value hidden; the agent cannot read or write it"
            + " — the user must type it in the Settings window's Extensions page)", set);
        Assert.Contains("\n- apiKey: secret text — NOT set (value hidden;", notSet);
    }

    // 政策的落点：即使结果里带着值，渲染也不许把它写出去。
    [Fact]
    public void SecretValueIsNeverRendered()
    {
        var text = Render(Extension(new JsonArray
        {
            new JsonObject
            {
                ["key"] = "apiKey",
                ["secret"] = true,
                ["secretSet"] = true,
                ["current"] = "sk-must-never-appear",
            },
        }));

        Assert.DoesNotContain("sk-must-never-appear", text);
    }

    [Fact]
    public void ExtensionWithNoFieldsSaysSoAtTheCurrentValues()
    {
        Assert.Equal("\"示例引擎\" declares no settings fields (at the current values).",
            Render(Extension(new JsonArray())));
    }

    // ── 错误路径

    // 测试进程里没有任何扩展声明设置（内建引擎都不实现 IExtensionSettings），故这条稳定命中"一个都没有"。
    // 关键是 message【不带 "Error: " 前缀】——前缀由入口加（agent 侧加、CLI 打 stderr 时不该冗余）。
    [Fact]
    public void UnknownExtensionFailsWithoutTheEntryPrefix()
    {
        var result = Command
            .ExecuteAsync(CommandArgs.Parse("""{"extension":"nope.nothing"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("no_extension_settings", result.Error!.Value.Code);
        Assert.DoesNotContain("Error:", result.Error!.Value.Message);
    }
}
