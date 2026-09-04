using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using TuneLab.Configs;
using Xunit;

namespace TuneLab.Tests;

// `setting set`（搬家前的 set_setting）的封条——它是第一条 edit 命令，故这里钉的多半是【所有 edit
// 命令都要守的形状】，而不只是这一条的措辞：
//  · 授权没通过不是错误，是如实汇报的结果（IsError=false + 闸门原话）。套成 CommandError 会给 agent
//    侧的回报凭空加一个 "Error: " 前缀，把"用户不让"读成"我调错了"。
//  · 入口没配授权策略时一条 Edit 都不做，且说清是"没配授权"而不是"用户拒绝"。
public class SettingSetCommandTests
{
    static readonly SettingSetCommand Command = new();

    // 两个不会落地的策略：既验证"没通过"这条路，也保证测试不会真去改开发机的设置。
    // 说辞不由它们给——那是命令面 IAuthorizationPolicy 的默认流程的事，这里正是要验那一份。
    sealed class ReadOnlyPolicy : IAuthorizationPolicy
    {
        public AuthorizationMode Mode => AuthorizationMode.ReadOnlyAdvice;
        public bool CanAsk => false;
        public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(AuthorizationDecision.Reject);
    }

    sealed class RejectingPolicy : IAuthorizationPolicy
    {
        public AuthorizationMode Mode => AuthorizationMode.Confirm;
        public bool CanAsk => true;
        public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(AuthorizationDecision.Reject);
    }

    static CommandResult Run(string argumentsJson, CommandContext? ctx = null)
        => Command.ExecuteAsync(CommandArgs.Parse(argumentsJson), ctx ?? new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

    // ── 参数与准入

    [Fact]
    public void UnknownKeyPointsAtTheList()
    {
        var result = Run("""{"key":"NoSuchSetting","value":1}""");

        Assert.True(result.IsError);
        Assert.Equal("unknown_key", result.Error!.Value.Code);
        Assert.Equal("no setting with key \"NoSuchSetting\". Call list_settings to see the exact keys.", result.Error!.Value.Message);
    }

    // 防自我提权：授权档位本身不可由 agent 改，且要转告用户去哪改。
    [Fact]
    public void NotAgentWritableSettingIsRefusedWithWhereToChangeIt()
    {
        var result = Run("""{"key":"AgentAuthorization","value":"Auto"}""");

        Assert.True(result.IsError);
        Assert.Equal("not_writable", result.Error!.Value.Code);
        Assert.Contains("cannot be changed by the agent — only the user can.", result.Error!.Value.Message);
        Assert.Contains("Tell the user where to change it themselves.", result.Error!.Value.Message);
    }

    [Fact]
    public void OutOfRangeValueIsRejectedBeforeAnyAuthorization()
    {
        var result = Run("""{"key":"AutoSaveInterval","value":9999}""");

        Assert.True(result.IsError);
        Assert.Equal("invalid_value", result.Error!.Value.Code);
    }

    [Fact]
    public void SettingItAlreadyHasChangesNothing()
    {
        var item = SettingsRegistry.All.First(i => i.Key == "AutoSaveInterval");
        item.GetValue().ToDouble(out var current);

        var result = Run("""{"key":"AutoSaveInterval","value":""" + (int)current + "}");

        Assert.False(result.IsError);
        Assert.Equal("unchanged", result.Data!["outcome"]!.GetValue<string>());
        Assert.Equal("The setting \"AutoSaveInterval\" is already " + (int)current + ". Nothing changed.",
            Command.Render(result.Data, CommandArgs.Empty));
    }

    // ── 授权

    // 关键：拒绝走【成功路径】，回报就是闸门那句原话。
    [Fact]
    public void RefusedChangeIsReportedAsAResultNotAnError()
    {
        var item = SettingsRegistry.All.First(i => i.Key == "AutoSaveInterval");
        item.GetValue().ToDouble(out var current);
        int other = (int)current == 10 ? 11 : 10;

        var result = Run("""{"key":"AutoSaveInterval","value":""" + other + "}",
            new CommandContext { Authorization = new RejectingPolicy() });

        Assert.False(result.IsError);
        Assert.Equal("refused", result.Data!["outcome"]!.GetValue<string>());
        Assert.Equal("The user chose NOT to allow it, so I did NOT change the setting \"AutoSaveInterval\" to " + other + ".",
            Command.Render(result.Data, CommandArgs.Empty));
        // 没落地：值还是原来的。
        item.GetValue().ToDouble(out var after);
        Assert.Equal(current, after);
    }

    // 只读建议档：不改，只说会改什么 + 怎么才能真改。
    [Fact]
    public void ReadOnlyModeExplainsHowToActuallyApplyIt()
    {
        var item = SettingsRegistry.All.First(i => i.Key == "AutoSaveInterval");
        item.GetValue().ToDouble(out var current);
        int other = (int)current == 10 ? 11 : 10;

        var result = Run("""{"key":"AutoSaveInterval","value":""" + other + "}",
            new CommandContext { Authorization = new ReadOnlyPolicy() });

        Assert.False(result.IsError);
        Assert.Equal("Authorization is READ-ONLY (advice mode): I did NOT change the setting \"AutoSaveInterval\" to " + other
            + ". Do it yourself, or raise agent authorization to Confirm or Auto.",
            Command.Render(result.Data, CommandArgs.Empty));
    }

    // 入口没给策略（headless/CI 忘了配）→ 一条 Edit 都不做，且不能说成"用户拒绝了"。
    [Fact]
    public void WithoutAPolicyNothingIsAllowedAndTheReasonSaysSo()
    {
        var (proceed, message) = new CommandContext()
            .Authorize(new AuthorizationRequest(WriteKind.SettingChange, 0, "AutoSaveInterval", "20"), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.False(proceed);
        Assert.Contains("no authorization policy configured", message);
        Assert.Contains("change the setting \"AutoSaveInterval\" to 20", message);
        Assert.DoesNotContain("The user", message);
    }

    // ── 落地后的措辞（用合成 Data 测，不去动开发机的设置文件）

    [Fact]
    public void AppliedChangeReportsBothValuesAndKeepsTheAuthorizationNote()
    {
        var text = Command.Render(new JsonObject
        {
            ["key"] = "AutoSaveInterval",
            ["outcome"] = "applied",
            ["old"] = 30,
            ["new"] = 20,
            ["restartRequired"] = false,
            ["note"] = "(The user was asked to confirm and approved this one; authorization stays at Confirm, so the next action will ask again.)\n",
        }, CommandArgs.Empty);

        Assert.StartsWith("(The user was asked to confirm and approved this one;", text);
        Assert.EndsWith("Changed \"AutoSaveInterval\" from 30 to 20 and saved the settings file.", text);
    }

    [Fact]
    public void RestartRequiredSettingSaysSo()
    {
        var text = Command.Render(new JsonObject
        {
            ["key"] = "Language",
            ["outcome"] = "applied",
            ["old"] = "en-US",
            ["new"] = "zh-CN",
            ["restartRequired"] = true,
            ["note"] = null,
        }, CommandArgs.Empty);

        Assert.Equal("Changed \"Language\" from \"en-US\" to \"zh-CN\" and saved the settings file."
            + " It only takes full effect after the user restarts TuneLab — tell them so.", text);
    }
}
