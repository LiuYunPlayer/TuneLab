using TuneLab.GUI.Controllers;
using Xunit;

namespace TuneLab.Tests;

// 文本字段收起时的摘要：首个非空行，其后还有内容补 "…"；全空白不显示。
public class TextFoldSummaryTests
{
    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("first\nsecond", "first …")]
    [InlineData("\n\nsecond", "second")]
    [InlineData("  \nsecond\n\n", "second")]
    [InlineData("first\n\n  \n", "first")]
    [InlineData("first\r\nsecond", "first …")]
    [InlineData("first\rsecond", "first …")]
    public void TakesFirstNonBlankLine(string text, string expected)
        => Assert.Equal(expected, PropertyObjectController.FirstLineSummary(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n \r\n")]
    public void AllBlankShowsNothing(string text)
        => Assert.Null(PropertyObjectController.FirstLineSummary(text));
}
