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

    // PATH 上那个名字来自安装器落的入口（TuneLab.Setup 的 CommandLineEntry）。便携解压的那份没有它，
    // 故不能一律声称能敲 tunelab —— 敲不通的一句"also on PATH"会让 agent 怀疑是自己搞错了。
    [Fact]
    public void OnlyClaimsThePathEntryWhenItIsReallyThere()
    {
        using var bridge = new Bridge(true);
        var text = ExternalAgentOnboarding.Build();

        bool claims = text.Contains("also on PATH");
        bool exists = ExternalAgentOnboarding.ExecutablePath != null
            && System.IO.File.Exists(System.IO.Path.Combine(
                TuneLab.PathManager.ExcutableFolder, "CommandLine", "tunelab.cmd"));
        Assert.Equal(exists, claims);
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
