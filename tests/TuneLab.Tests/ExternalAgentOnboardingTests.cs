using System;
using TuneLab.Configs;
using TuneLab.UI;
using Xunit;

namespace TuneLab.Tests;

// 设置窗里那颗"复制"按钮吐出去的文案（ExternalAgentOnboarding）。它会被用户原样贴进他自己雇的
// agent 里，故这里钉的是三件"说了就必须是真的"的事：
//  · 路径要么是真的，要么明说这份安装没有命令行 —— 一条无效路径会让那个 agent 试到放弃；
//  · `tunelab` 只有在真落过入口时才敢声称；
//  · 桥没开时必须先说"这件事只有我能做"，否则 agent 会拿着一条连不上的命令反复重试。
public class ExternalAgentOnboardingTests
{
    // Settings 是进程级的，改完必须还原：同一个进程里还跑着别的测试。
    sealed class Bridge : IDisposable
    {
        readonly bool mPrevious = Settings.CommandBridgeEnabled.Value;
        public Bridge(bool on) => Settings.CommandBridgeEnabled.Value = on;
        public void Dispose() => Settings.CommandBridgeEnabled.Value = mPrevious;
    }

    [Fact]
    public void EitherGivesTheRealPathOrSaysThereIsNoCommandLine()
    {
        using var bridge = new Bridge(true);
        var text = ExternalAgentOnboarding.Build();

        if (ExternalAgentOnboarding.ExecutablePath is { } exe)
            Assert.Contains(exe, text);
        else
            Assert.Contains("no command line", text);
    }

    // 一期刻意不动用户的 PATH（命令行靠绝对路径就能跑，PATH 只是省几个字），故这段话【不许】
    // 声称能直接敲 tunelab —— 敲不通的一句 "on PATH" 会让 agent 怀疑是自己搞错了。示例里仍写
    // `tunelab`（短、可读），但必须同时把"它就是上面那个路径"说出来，别让读的人自己去推。
    [Fact]
    public void NeverPromisesThatTunelabIsOnPath()
    {
        using var bridge = new Bridge(true);
        var text = ExternalAgentOnboarding.Build();

        Assert.DoesNotContain("on PATH as", text);
        Assert.Contains("it is not on PATH, so substitute it", text);
    }

    [Fact]
    public void SaysWhoHasToTurnTheBridgeOnWhenItIsOff()
    {
        using var bridge = new Bridge(false);
        var text = ExternalAgentOnboarding.Build();

        // 桥没开时：先说清只有用户做得到，且点出"文档命令现在就能用"——那是 agent 立刻能做的事。
        Assert.Contains("command bridge is off", text);
        Assert.Contains("Only I can turn it on", text);
        Assert.Contains("documentation commands below still work", text);
    }

    [Fact]
    public void SaysNothingAboutTurningItOnWhenItIsAlreadyOn()
    {
        using var bridge = new Bridge(true);
        var text = ExternalAgentOnboarding.Build();

        Assert.DoesNotContain("command bridge is off", text);
    }
}
