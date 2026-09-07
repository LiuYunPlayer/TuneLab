using System.IO;
using System.Linq;
using Tomlyn;
using Xunit;

namespace TuneLab.Tests;

// 随包翻译文件的封条。
//
// 为什么值得单独一条：TomlTranslator 在 Toml.TryToModel 失败时【静默返回】——一个手抖的引号不会
// 报任何错，只会让那门语言的整份词典变成空的，界面悄悄退回英文。改这些文件的人（往往是批量补 15
// 份译文）当场看不出来，而用那门语言的用户也未必会来说。
public class TranslationFileTests
{
    [Fact]
    public void EveryShippedTranslationFileParses()
    {
        var folder = TuneLab.PathManager.TranslationsFolder;
        Assert.True(Directory.Exists(folder), "Resources/Translations is missing from the test output: " + folder);

        var files = Directory.GetFiles(folder, "*.toml").OrderBy(p => p).ToArray();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            Assert.True(Toml.TryToModel<Tomlyn.Model.TomlTable>(File.ReadAllText(file), out _, out var diagnostics),
                Path.GetFileName(file) + " does not parse, so that whole language would silently fall back to English:\n"
                + diagnostics);
        }
    }
}
