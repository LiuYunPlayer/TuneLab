using TuneLab.Configs;
using Xunit;

namespace TuneLab.Tests;

// preset 名 = 文件名的合法性校验（PresetConfigManager.GetPresetNameError）。
// 按 Windows 超集、各平台一致：非法字符 / 保留设备名 / 首尾空格与尾点 / 空 / 超长。
public class PresetNameValidationTests
{
    [Theory]
    [InlineData("My Preset")]
    [InlineData("默认音色-01")]
    [InlineData("soft.bright")]
    [InlineData("CONCERT")]      // 前缀撞保留名但整段不等 → 合法
    public void ValidNames_PassValidation(string name)
    {
        Assert.Null(PresetConfigManager.GetPresetNameError(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b>")]
    [InlineData("a|b")]
    [InlineData("ends.")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("Nul.backup")]   // 保留名占第一段（Windows 连带扩展名一起保留）
    [InlineData("COM3")]
    [InlineData("lpt9")]
    public void InvalidNames_AreRejected(string name)
    {
        Assert.NotNull(PresetConfigManager.GetPresetNameError(name));
    }

    [Fact]
    public void OverlongName_IsRejected()
    {
        Assert.NotNull(PresetConfigManager.GetPresetNameError(new string('x', 101)));
        Assert.Null(PresetConfigManager.GetPresetNameError(new string('x', 100)));
    }
}
