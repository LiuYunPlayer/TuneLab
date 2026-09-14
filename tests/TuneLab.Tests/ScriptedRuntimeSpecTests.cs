using TuneLab.Extensions.Scripted;
using Xunit;

namespace TuneLab.Tests;

// manifest `runtime` 串的解析与区间判定（设计文档 §8.1）。
public class ScriptedRuntimeSpecTests
{
    [Fact]
    public void ParsesFlavorWithConstraintList()
    {
        Assert.True(RuntimeSpec.TryParse("python>=3.10,<4", out var spec, out var error));
        Assert.Null(error);
        Assert.Equal("python", spec.Flavor);
        Assert.True(spec.HasMin);
        Assert.True(spec.HasMax);
        Assert.Equal("python>=3.10.0,<4.0.0", spec.Describe());
    }

    [Theory]
    [InlineData("3.12.7", true)]
    [InlineData("3.10", true)]
    [InlineData("4.0", false)]  // 上界开
    [InlineData("3.9.9", false)]
    public void RangeMembership(string version, bool expected)
    {
        Assert.True(RuntimeSpec.TryParse("python>=3.10,<4", out var spec, out _));
        Assert.Equal(expected, spec.SatisfiedBy(version));
    }

    [Fact]
    public void BareFlavorMeansUnconstrained()
    {
        Assert.True(RuntimeSpec.TryParse("node", out var spec, out _));
        Assert.False(spec.HasMin);
        Assert.False(spec.HasMax);
        Assert.True(spec.SatisfiedBy("20.11.0"));
    }

    [Fact]
    public void EqualityConstraintIsExactOnThreeParts()
    {
        Assert.True(RuntimeSpec.TryParse("lua==5.4", out var spec, out _));
        Assert.True(spec.SatisfiedBy("5.4"));
        Assert.False(spec.SatisfiedBy("5.5"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ruby>=3")]      // 未知 flavor
    [InlineData("python~3")]     // 未知操作符
    [InlineData("python>=x.y")]  // 坏版本
    public void MalformedIsRejectedWithString(string text)
    {
        Assert.False(RuntimeSpec.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("PYTHON >= 3.10", true)]   // 大小写与空白宽容
    [InlineData("luajit>=2.1,<2.2", true)]
    public void NormalizesDeclaration(string text, bool expected)
        => Assert.Equal(expected, RuntimeSpec.TryParse(text, out _, out _));
}
