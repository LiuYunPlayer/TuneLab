using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `extension set-routing` / `extension enable` / `extension set-setting`（搬家前的
// set_extension_routing / set_extension_enabled / set_extension_setting）的封条。
// 落地那步会写用户的应用配置，故用合成 Data 测渲染 + 几条不可能落地的错误路径。
//
// 这三条的措辞里，两类话绝对不能丢：
//  · 【重启才生效】它们都是"存下了，但这次运行不算数"——不说清楚，用户会以为没生效而重复操作；
//  · 【关掉意味着什么】整包关掉 = 它的能力这次运行一个都不存在；单个能力关掉 ≠ 整包关掉。
public class ExtensionWriteCommandsTests
{
    // ── extension set-routing

    static readonly ExtensionSetRoutingCommand Routing = new();

    [Fact]
    public void SelectingAProviderSaysItNeedsARestartAndHowToVerify()
    {
        var text = Routing.Render(new JsonObject
        {
            ["route"] = "voice:v1.voice",
            ["outcome"] = "applied",
            ["cleared"] = false,
            ["packageId"] = "pkg.a",
            ["target"] = "包甲",
            ["note"] = null,
        }, CommandArgs.Empty);

        Assert.Equal("Selected \"包甲\" for voice:v1.voice. Saved, but it only takes effect after TuneLab restarts"
            + " — tell the user to restart, then verify with list_extension_routing.", text);
    }

    // 清除选择要说清"回默认规则后当前会落到谁"——否则用户不知道自己清出了什么结果。
    [Fact]
    public void ClearingTheChoiceNamesWhatTheDefaultRuleResolvesTo()
    {
        var text = Routing.Render(new JsonObject
        {
            ["route"] = "format-import:mid",
            ["outcome"] = "applied",
            ["cleared"] = true,
            ["packageId"] = null,
            ["target"] = "内建",
            ["note"] = null,
        }, CommandArgs.Empty);

        Assert.StartsWith("Cleared the package choice (back to the default rule, which currently resolves to \"内建\") for format-import:mid.", text);
    }

    [Fact]
    public void RoutingThatIsAlreadySetChangesNothing()
    {
        Assert.Equal("\"voice:v1.voice\" is already set to \"包甲\". Nothing changed.", Routing.Render(new JsonObject
        {
            ["route"] = "voice:v1.voice",
            ["outcome"] = "unchanged",
            ["cleared"] = false,
            ["target"] = "包甲",
        }, CommandArgs.Empty));
    }

    // 没有任何冲突时不该让调用方继续折腾路由，而要把它引向真正的排查面。
    [Fact]
    public void RoutingWithoutAnyConflictPointsElsewhere()
    {
        var result = Routing.ExecuteAsync(CommandArgs.Parse("""{"kind":"voice","identity":"whatever"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("no_conflicts", result.Error!.Value.Code);
        Assert.Contains("check list_extensions for load errors instead", result.Error!.Value.Message);
    }

    // ── extension enable

    static readonly ExtensionEnableCommand Enable = new();

    [Fact]
    public void DisablingAPackageWarnsItStaysUsableUntilRestart()
    {
        var text = Enable.Render(new JsonObject
        {
            ["target"] = "示例包",
            ["secondaryTarget"] = null,
            ["scope"] = "package",
            ["enabled"] = false,
            ["outcome"] = "applied",
            ["note"] = null,
        }, CommandArgs.Empty);

        Assert.Equal("Disabled the extension \"示例包\". Saved, but it only takes effect after TuneLab restarts"
            + " — tell the user to restart, then verify with list_extensions. Until then it stays loaded and usable this session.", text);
    }

    // 开启不需要那句"这次运行还能用"（它本来就没被关掉过这一说）。
    [Fact]
    public void EnablingOneCapabilityNamesItsPackageAndOmitsTheStillUsableNote()
    {
        var text = Enable.Render(new JsonObject
        {
            ["target"] = "voice:v1.voice",
            ["secondaryTarget"] = "示例包",
            ["scope"] = "capability",
            ["enabled"] = true,
            ["outcome"] = "applied",
            ["note"] = null,
        }, CommandArgs.Empty);

        Assert.StartsWith("Enabled the \"voice:v1.voice\" capability of \"示例包\".", text);
        Assert.DoesNotContain("stays loaded and usable", text);
    }

    // 整包关着时开单个能力：不能写下一个看不出效果的选择，要说清先开整包。
    [Fact]
    public void EnablingACapabilityOfADisabledPackageSaysToEnableThePackageFirst()
    {
        Assert.Equal("The whole extension \"示例包\" is disabled, so \"voice:v1.voice\" cannot be enabled on its own."
            + " Enable the package first (call again without \"capability\").", Enable.Render(new JsonObject
        {
            ["target"] = "voice:v1.voice",
            ["secondaryTarget"] = "示例包",
            ["scope"] = "capability",
            ["enabled"] = true,
            ["outcome"] = "unchanged",
            ["reason"] = "package_disabled_cannot_enable",
        }, CommandArgs.Empty));
    }

    [Fact]
    public void DisablingACapabilityOfAnAlreadyDisabledPackageSaysWhyItIsAlreadyOff()
    {
        Assert.Equal("\"voice:v1.voice\" is already off, because the whole extension \"示例包\" is disabled. Nothing changed.",
            Enable.Render(new JsonObject
            {
                ["target"] = "voice:v1.voice",
                ["secondaryTarget"] = "示例包",
                ["scope"] = "capability",
                ["enabled"] = false,
                ["outcome"] = "unchanged",
                ["reason"] = "package_disabled_already_off",
            }, CommandArgs.Empty));
    }

    // 启停是二选一的破坏性动作，缺 enabled 不猜默认值。
    [Fact]
    public void MissingEnabledFlagIsRejectedRatherThanGuessed()
    {
        var result = Enable.ExecuteAsync(CommandArgs.Parse("""{"packageId":"pkg.a"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("missing_enabled", result.Error!.Value.Code);
    }

    [Fact]
    public void UnknownPackageListsWhatIsInstalled()
    {
        var result = Enable.ExecuteAsync(CommandArgs.Parse("""{"packageId":"no.such.pkg","enabled":false}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("unknown_package", result.Error!.Value.Code);
        Assert.Contains("Installed ids:", result.Error!.Value.Message);
    }

    // ── extension set-setting

    static readonly ExtensionSetSettingCommand SetSetting = new();

    [Fact]
    public void ChangedFieldMentionsTheImmediateHandoffAndThePossibleRestart()
    {
        var text = SetSetting.Render(new JsonObject
        {
            ["extension"] = "示例引擎",
            ["key"] = "device",
            ["outcome"] = "applied",
            ["old"] = "cpu",
            ["new"] = "cuda",
            ["note"] = null,
        }, CommandArgs.Empty);

        Assert.Equal("Changed \"device\" of \"示例引擎\" from \"cpu\" to \"cuda\" and saved it;"
            + " the extension was handed the new settings immediately."
            + " If it seems to have no effect, the engine may only read it when it next starts — tell the user they can restart TuneLab.", text);
    }

    [Fact]
    public void FieldThatAlreadyHasTheValueChangesNothing()
    {
        Assert.Equal("\"device\" of \"示例引擎\" is already \"cuda\". Nothing changed.", SetSetting.Render(new JsonObject
        {
            ["extension"] = "示例引擎",
            ["key"] = "device",
            ["outcome"] = "unchanged",
            ["old"] = "cuda",
            ["new"] = "cuda",
        }, CommandArgs.Empty));
    }

    // 密钥政策的写那一半：拒写，并说清该由用户自己去设置窗填。
    // （测试进程里没有装扩展，故这里只能验到定位失败那一步；密钥拒写的措辞由读那边的封条与本命令的
    //  Fail 分支共同保证——见 ExtensionSettingsCommandTests。）
    [Fact]
    public void SettingAFieldWithoutAnyExtensionInstalledFailsWithoutThePrefix()
    {
        var result = SetSetting
            .ExecuteAsync(CommandArgs.Parse("""{"extension":"whatever","key":"device","value":"cuda"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("no_extension_settings", result.Error!.Value.Code);
        Assert.DoesNotContain("Error:", result.Error!.Value.Message);
    }
}
