using System;
using System.IO;
using System.Linq;
using TuneLab.Extensions;
using Xunit;

namespace TuneLab.Tests;

// sdk-version「一个发布周期只占一档」的封条（规矩见 docs/sdk-api-evolution.md §0）。
//
// 判据用现成的台账：SDK / Foundation 的 PublicAPI.Unshipped.txt 有内容 = 本周期已有新 API = 该已提过档。
// 于是忘了提（有新 API、版本没动）与重复提（同一周期又提一档）都会在这里挂掉，不靠人记得去查。
public class SdkVersionLedgerTests
{
    [Fact]
    public void SdkVersionIsOneStepAboveTheLastReleaseExactlyWhenThereIsUnshippedApi()
    {
        var root = RepoRoot();
        bool unshipped = HasUnshippedApi(root, "TuneLab.SDK") || HasUnshippedApi(root, "TuneLab.Foundation");
        var current = ExtensionManager.SdkVersion;
        var released = ExtensionManager.LastReleasedSdkVersion;
        var next = new Version(released.Major, released.Minor + 1);

        if (unshipped)
        {
            Assert.True(current != released, string.Format(
                "PublicAPI.Unshipped.txt has new API but SdkVersion is still {0}, the last released one. Bump it to {1} (once per release cycle).",
                current, next));
            Assert.True(current == next, string.Format(
                "SdkVersion {0} is more than one step above the last release {1}. New API within the same release cycle joins {2}; don't bump again.",
                current, released, next));
        }
        else
        {
            Assert.True(current == released, string.Format(
                "There is no unshipped API, yet SdkVersion {0} differs from LastReleasedSdkVersion {1}. If a release just shipped, set LastReleasedSdkVersion to {0}; otherwise undo the bump.",
                current, released));
        }
    }

    // 版本表是插件作者查"该声明哪一档"的地方：当前这一档必须在上面。
    [Fact]
    public void DeveloperDocsListTheCurrentSdkVersion()
    {
        var root = RepoRoot();
        var version = ExtensionManager.SdkVersion.ToString(2);
        foreach (var doc in new[] { "plugin-development.md", "plugin-development.zh-CN.md" })
            Assert.Contains("| `" + version + "` |", File.ReadAllText(Path.Combine(root, "docs", doc)));
        Assert.Contains("`\"" + version + "\"`", File.ReadAllText(Path.Combine(root, "docs", "plugin-development-llm.md")));
    }

    // 有效行 = 去掉空行与 #nullable 之类的指令行之后剩下的签名。
    static bool HasUnshippedApi(string root, string project)
        => File.ReadAllLines(Path.Combine(root, project, "PublicAPI.Unshipped.txt"))
            .Select(line => line.Trim())
            .Any(line => line.Length > 0 && !line.StartsWith('#'));

    // 仓库根：从测试程序集所在目录往上找 TuneLab.sln。这条封条查的是源码台账，故必须在仓库里跑。
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TuneLab.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
