using System.Linq;
using TuneLab.Docs;
using Xunit;

namespace TuneLab.Tests;

// 随包用户手册的加载与切分口径：应用内手册窗与 agent 的 get_manual 都从这里取章，故这里钉死
// 「章从哪切、id 是什么、喂模型的文本剥掉了什么」。手册文件本体（docs/user-manual.zh-CN.md）由
// TuneLab.csproj 以 Link 落进输出目录的 Resources/Manual，测试跑的就是随包的那一份——
// 手册若丢了锚点注释或章节被改名，这些断言会先叫。
public class ManualLibraryTests
{
    [Fact]
    public void ManualIsBundled()
    {
        Assert.True(ManualLibrary.IsAvailable);
        Assert.False(string.IsNullOrEmpty(ManualLibrary.Language));
    }

    [Fact]
    public void SectionsAreSplitByChapterWithStableIds()
    {
        var sections = ManualLibrary.Sections;

        // 章数不做精确断言（手册会长），但少于这个数就说明切分坏了而不是内容少了。
        Assert.True(sections.Count >= 15, $"only {sections.Count} sections parsed");

        // 每章都必须有锚点注释给出的稳定 id（slug 兜底会让中文标题退化成一串连字符，故这里排除它）。
        foreach (var section in sections)
        {
            Assert.False(string.IsNullOrWhiteSpace(section.Id));
            Assert.DoesNotContain("--", section.Id);
            Assert.False(string.IsNullOrWhiteSpace(section.Title));
            Assert.False(string.IsNullOrWhiteSpace(section.Body));
        }

        Assert.Equal(sections.Select(s => s.Id).Distinct().Count(), sections.Count);
    }

    [Fact]
    public void NavigationChapterIsNotASection()
    {
        // 「目录」对读者是导航、对模型是重复信息：本类自己产目录，故它不该作为一章出现。
        Assert.DoesNotContain(ManualLibrary.Sections, s => s.Title == "目录");
    }

    [Fact]
    public void ChapterTitlesDropTheirNumbering()
    {
        // 章号会随插章变动，不进标题也不进 id。
        Assert.DoesNotContain(ManualLibrary.Sections, s => s.Title.StartsWith("1.") || s.Title.StartsWith("10."));
    }

    [Theory]
    [InlineData("parameters")]
    [InlineData("PARAMETERS")]      // id 大小写不敏感
    [InlineData("参数面板")]         // 标题子串
    public void FindResolvesIdAndTitle(string key)
    {
        var section = ManualLibrary.Find(key);
        Assert.NotNull(section);
        Assert.Equal("parameters", section!.Id);
    }

    [Fact]
    public void FindResolvesSubheading()
    {
        // 子节标题也能定位到所属章（模型往往只知道功能名，不知道它归哪一章）。
        var section = ManualLibrary.Find("歌词与发音");
        Assert.NotNull(section);
        Assert.Equal("notes-and-lyrics", section!.Id);
    }

    [Fact]
    public void FindReturnsNullForUnknownKey()
    {
        Assert.Null(ManualLibrary.Find("这个章节不存在"));
        Assert.Null(ManualLibrary.Find(" "));
    }

    [Fact]
    public void ForModelStripsImagesAndComments()
    {
        var section = ManualLibrary.Find("ui-overview");
        Assert.NotNull(section);

        // 原文里有插图，喂模型的文本里不该有——图对模型是噪声。
        Assert.Contains("![", section!.Body);
        var text = ManualLibrary.ForModel(section);
        Assert.DoesNotContain("![", text);
        Assert.DoesNotContain("<!--", text);
        Assert.Contains(section.Title, text);
    }

    [Fact]
    public void TocListsEveryChapterId()
    {
        var toc = ManualLibrary.BuildToc();
        foreach (var section in ManualLibrary.Sections)
            Assert.Contains(section.Id, toc);
    }

    [Fact]
    public void SearchReportsTheChapterOfEachHit()
    {
        var hits = ManualLibrary.Search("固定笔刷");
        Assert.NotEmpty(hits);
        Assert.All(hits, hit =>
        {
            Assert.Contains("固定笔刷", hit.Line);
            Assert.NotNull(ManualLibrary.Find(hit.SectionId));
        });
    }

    // 正文里的跳转链接（](#锚点)）在应用内手册窗里按章滚动：每一个都得落到某一章——改了标题而没改链接，这里先叫。
    [Fact]
    public void EveryInManualLinkResolvesToAChapter()
    {
        var sections = ManualLibrary.Sections;
        var anchors = sections
            .SelectMany(s => System.Text.RegularExpressions.Regex.Matches(s.Body, @"\]\(#([^)]+)\)").Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();
        Assert.NotEmpty(anchors);
        var unresolved = anchors.Where(a => ManualLibrary.IndexOfAnchor(sections, a) < 0).ToList();
        Assert.True(unresolved.Count == 0, "links that point at no chapter: " + string.Join(", ", unresolved));
    }

    // 锚点规则同 GitHub：小写、去标点、空格变连字符，中文保留；章标题里的章号在比对时去掉。
    [Fact]
    public void AnchorsFollowGitHubHeadingRules()
    {
        Assert.Equal("123-外观", ManualLibrary.AnchorOf("12.3 外观"));
        Assert.Equal("范围选区daw-式常驻选区", ManualLibrary.AnchorOf("范围选区（DAW 式常驻选区）"));
        var sections = ManualLibrary.Sections;
        Assert.Equal(ManualLibrary.IndexOfAnchor(sections, "扩展管理"), ManualLibrary.IndexOfAnchor(sections, "15-扩展管理"));
        Assert.Equal(-1, ManualLibrary.IndexOfAnchor(sections, "no-such-heading"));
    }

    [Fact]
    public void SearchIsEmptyForBlankOrMissingQuery()
    {
        Assert.Empty(ManualLibrary.Search(""));
        Assert.Empty(ManualLibrary.Search("zzzzz-not-in-the-manual"));
    }
}
