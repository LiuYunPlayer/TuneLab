using System;
using System.Collections.Generic;
using TuneLab;
using Xunit;

namespace TuneLab.Tests;

// 启动参数分流（StartupArgs）的封条。原先每个参数都直接送去"打开工程"，于是传四个非文件的词就弹四个
// "文件打不开"的框——而用户压根没想打开文件。这里钉住四条判据：选项被忽略、.tlx 可多个、工程只认第一个、
// 打不开且不像我们的文件就不当文件。
public class StartupArgsTests
{
    // 真实实现是"文件存在 or 后缀是已注册的导入格式"；测试里只认这几个后缀，不碰磁盘也不碰插件注册表。
    static bool LooksLikeProject(string path)
        => path.EndsWith(".tlp", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tlpx", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase);

    static StartupArgs.Plan Split(params string[] args) => StartupArgs.Split(args, LooksLikeProject);

    [Fact]
    public void DoubleClickingAProjectOpensIt()
    {
        var plan = Split(@"C:\songs.tlp");

        Assert.Equal(@"C:\songs.tlp", plan.ProjectPath);
        Assert.Empty(plan.ExtensionPackages);
        Assert.Empty(plan.Ignored);
    }

    // 一个窗口一次只开一个工程：多给的如实记为忽略，而不是逐个打开（那会连开几次、并弹几个框）。
    [Fact]
    public void OnlyTheFirstProjectIsOpenedTheRestAreIgnored()
    {
        var plan = Split(@"C:.tlp", @"C:.tlpx", @"C:\c.mid");

        Assert.Equal(@"C:.tlp", plan.ProjectPath);
        Assert.Equal(new List<string> { @"C:.tlpx", @"C:\c.mid" }, plan.Ignored);
    }

    // .tlx 是扩展包，双击即安装，可以一次装多个（与工程的"只认一个"相反）。
    [Fact]
    public void EveryExtensionPackageIsCollected()
    {
        var plan = Split(@"C:\one.tlx", @"C:	wo.tlx", @"C:\song.tlp");

        Assert.Equal(new List<string> { @"C:\one.tlx", @"C:	wo.tlx" }, plan.ExtensionPackages);
        Assert.Equal(@"C:\song.tlp", plan.ProjectPath);
        Assert.Empty(plan.Ignored);
    }

    // 【本次的 bug】命令行式的一串词：既不是选项也不是我们认得的文件，一个都不该当工程去打开。
    [Fact]
    public void CommandLikeWordsAreIgnoredInsteadOfOpened()
    {
        var plan = Split("action", "list", "--query", "transpose");

        Assert.Null(plan.ProjectPath);
        Assert.Empty(plan.ExtensionPackages);
        Assert.Equal(4, plan.Ignored.Count);
    }

    // 选项一律不当文件，即便后缀恰好像工程（`--open=a.tlp` 这种）。主程序今天没有自己的选项，将来要加
    // 就在这条判据后面接。
    [Fact]
    public void OptionsAreNeverTakenAsFiles()
    {
        var plan = Split("--headless", "-x", "--open=a.tlp");

        Assert.Null(plan.ProjectPath);
        Assert.Equal(3, plan.Ignored.Count);
    }

    // 打不开但**像**我们的文件（用户双击了一个已被删除/移走的工程）仍然交给打开流程——那句"文件不存在"
    // 是他要知道的事。这里 looksLikeProject 认后缀，故不存在的 .tlp 照样进 ProjectPath。
    [Fact]
    public void AMissingButKnownProjectStillGoesThroughSoTheUserGetsTold()
    {
        var plan = Split(@"X:\deleted\song.tlp");

        Assert.Equal(@"X:\deleted\song.tlp", plan.ProjectPath);
        Assert.Empty(plan.Ignored);
    }

    [Fact]
    public void BlanksAreDroppedSilently()
    {
        var plan = Split("", "   ", @"C:.tlp");

        Assert.Equal(@"C:.tlp", plan.ProjectPath);
        Assert.Empty(plan.Ignored);
    }
}
