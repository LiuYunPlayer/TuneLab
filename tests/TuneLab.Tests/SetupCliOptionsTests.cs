using System.Linq;
using TuneLab.Setup;
using TuneLab.Setup.Core;
using Xunit;

namespace TuneLab.Tests;

// 安装器命令行的封条。
//
// 【它要钉的是一条约定】命令行是**向导的完整镜像**：向导上能勾的每一项都有对应参数，而且**缺省值与
// 向导的初始值一致**。后半句是机器可检的——向导那几个复选框的初始状态就是 InstallOptions 的字段默认值，
// 所以这里逐项对着它断言：谁改了向导的默认而没改命令行（或反过来），这条就红。
//
// 少一项参数会逼着自动化的人回去点界面；默认值两边不一致更糟——照着界面理解的行为，在脚本里会变成
// 另一回事，而那种偏差要装完一遍才看得见。
public class SetupCliOptionsTests
{
    [Fact]
    public void NoArgumentsMeansTheWizard()
    {
        var options = CliOptions.Parse([]);

        Assert.Equal(SetupMode.Interactive, options.Mode);
        Assert.Null(options.Error);
        Assert.False(options.Help);
    }

    // 这条是那句约定本身：-silent 什么都不给时，每一项都等于向导的初始值。
    [Fact]
    public void SilentDefaultsAreExactlyWhatTheWizardStartsWith()
    {
        var cli = CliOptions.Parse(["-silent"]);
        var wizard = new InstallOptions();

        Assert.Equal(SetupMode.Silent, cli.Mode);
        Assert.Equal(wizard.CreateDesktopShortcut, cli.DesktopShortcut);
        Assert.Equal(wizard.CreateStartMenuShortcut, cli.StartMenuShortcut);
        Assert.Equal(wizard.RegisterFileAssociations, cli.FileAssociations);
        Assert.Equal(wizard.LaunchAfterInstall, cli.LaunchAfterInstall);
        // 目录不给就是向导填好的那个默认目录（这里只断言"没自己编一个"）。
        Assert.Null(cli.TargetDir);
        // 语言不给就不动用户已有的设置。
        Assert.Null(cli.Language);
    }

    [Fact]
    public void EveryWizardCheckboxHasASwitch()
    {
        var options = CliOptions.Parse([
            "-silent",
            "-dir", @"C:\somewhere",
            "-desktop-shortcut", "false",
            "-start-menu-shortcut", "false",
            "-file-assoc", "false",
            "-launch", "false",
            "-language", "zh-CN",
        ]);

        Assert.Null(options.Error);
        Assert.Equal(SetupMode.Silent, options.Mode);
        Assert.Equal(@"C:\somewhere", options.TargetDir);
        Assert.False(options.DesktopShortcut);
        Assert.False(options.StartMenuShortcut);
        Assert.False(options.FileAssociations);
        Assert.False(options.LaunchAfterInstall);
        Assert.Equal("zh-CN", options.Language);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("on")]
    public void TheUsualWaysOfSayingYesAllWork(string yes)
    {
        Assert.True(CliOptions.Parse(["-silent", "-desktop-shortcut", yes]).DesktopShortcut);
    }

    // 认不出的值不当成 false——"我以为关掉了，其实开着"是这里最坏的一种错。
    [Fact]
    public void AValueThatIsNeitherTrueNorFalseIsAUsageError()
    {
        var options = CliOptions.Parse(["-silent", "-desktop-shortcut", "maybe"]);

        Assert.NotNull(options.Error);
        Assert.Contains("true or false", options.Error);
    }

    [Fact]
    public void AnUnknownOptionIsNotSilentlyIgnored()
    {
        var options = CliOptions.Parse(["-silent", "-shortcuts"]);

        Assert.NotNull(options.Error);
        Assert.Contains("-shortcuts", options.Error);
    }

    [Fact]
    public void AnOptionMissingItsValueSaysSo()
    {
        var options = CliOptions.Parse(["-silent", "-dir"]);

        Assert.NotNull(options.Error);
        Assert.Contains("needs a value", options.Error);
    }

    // 卸载与自更新是另外两条无界面的路，-silent 不该把它们改掉。
    [Fact]
    public void SilentDoesNotChangeTheUninstallOrUpdateModes()
    {
        Assert.Equal(SetupMode.Uninstall, CliOptions.Parse(["-uninstall", @"C:\dir", "-silent"]).Mode);
        Assert.Equal(SetupMode.Update, CliOptions.Parse(["-update", @"C:\dir", "-silent"]).Mode);
    }

    // 用法里必须把每个参数和它的默认值都写出来——这是无人值守的人唯一的说明书。
    [Fact]
    public void TheUsageTextListsEveryOptionAndTheLanguages()
    {
        var usage = CliOptions.Usage(SetupI18N.SupportedLanguages);

        foreach (var option in new[] { "-silent", "-dir", "-desktop-shortcut", "-start-menu-shortcut", "-file-assoc", "-launch", "-language", "-uninstall" })
            Assert.Contains(option, usage);
        Assert.Contains("zh-CN", usage);
        Assert.Contains("Exit codes", usage);
    }

    [Fact]
    public void TheLanguageCodesTheCommandLineAcceptsAreTheOnesItShips()
    {
        Assert.Contains("zh-CN", SetupI18N.SupportedLanguages);
        Assert.Equal("zh-CN", SetupI18N.Match("zh-CN"));
        Assert.Null(SetupI18N.Match("xx-YY"));
    }
}
