using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using TuneLab.Extensions.Scripted;
using TuneLab.Utils;
using Xunit;

namespace TuneLab.Tests;

// extension fetch-runtime 的拒绝性一侧（坏 runtime / 无格子 / 用户不授权）——都在触网之前落判，
// 成功下载链由 ScriptedRuntimeFetcherTests 覆盖（不在此重复真网络）。
public class ExtensionFetchRuntimeCommandTests
{
    sealed class AlwaysAskPolicy : IAuthorizationPolicy
    {
        public AuthorizationRequest? Asked;
        public AuthorizationMode Mode => AuthorizationMode.Confirm;
        public bool CanAsk => true;
        public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken ct)
        {
            Asked = request;
            return Task.FromResult(AuthorizationDecision.Reject);
        }
    }

    static Task<CommandResult> Run(ExtensionFetchRuntimeCommand cmd, string argsJson, CommandContext ctx)
        => cmd.ExecuteAsync(CommandArgs.Parse(argsJson), ctx, CancellationToken.None);

    [Fact]
    public void MalformedRuntimeSpecIsRefused()
    {
        var r = Run(new ExtensionFetchRuntimeCommand(), "{\"runtime\":\"ruby>=3\"}", new CommandContext())
            .GetAwaiter().GetResult();
        Assert.True(r.IsError);
        Assert.Equal("bad_runtime", r.Error!.Value.Code);
    }

    [Fact]
    public void UnsatisfiableFeedCellRefusesBeforeAsking()
    {
        var policy = new AlwaysAskPolicy();
        var r = Run(new ExtensionFetchRuntimeCommand(), "{\"runtime\":\"lua>=5.4\"}", new CommandContext { Authorization = policy })
            .GetAwaiter().GetResult();
        Assert.True(r.IsError);
        Assert.Equal("no_feed_cell", r.Error!.Value.Code);   // feed 无 lua 格子 → 不弹授权不触网
        Assert.Null(policy.Asked);
    }

    [Fact]
    public async Task FetchAsksBeforeTouchingNetwork()
    {
        Assert.True(RuntimeSpec.TryParse("python>=3.10,<4", out var spec, out _));
        var policy = new AlwaysAskPolicy();
        var cmd = new ExtensionFetchRuntimeCommand();
        var r = await Run(cmd, "{\"runtime\":\"python>=3.10,<4\"}", new CommandContext { Authorization = policy });
        if (RuntimeFeed.Match(spec) is null)
        {
            Assert.True(r.IsError);                       // 本平台无格子：直接如实拒绝
            Assert.Null(policy.Asked);
        }
        else
        {
            Assert.NotNull(policy.Asked);                 // 有格子：授权卡片先于任何下载
            Assert.Equal(WriteKind.ExtensionInstall, policy.Asked!.Value.Kind);
            Assert.False(r.IsError);
            Assert.Equal("refused", r.Data!["outcome"]!.GetValue<string>());
        }
    }
}
