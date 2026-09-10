using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using TuneLab.Setup.Core;
using Xunit;

namespace TuneLab.Tests;

// 安装清单的封条。
//
// 【它守的是什么】安装目录是调用方给的——`-silent -dir D:\Apps` 就把 D:\Apps 整个交了出来。卸载若对
// 这个目录做递归删除，删掉的就不止是安装器铺的那些文件。清单把删除范围从"某个目录"改成"我们自己铺过
// 的那批文件"，这里逐条钉住那个判据：认领得到的才删，认领不到的一件都不动。
//
// 删错文件拿不回来，所以这些断言的方向一律是"宁可少删"：认不出的清单当没有清单、越界的条目直接丢掉、
// 没有账又不是自己造的目录名就不动手。
[SupportedOSPlatform("windows")]
public class SetupInstallManifestTests
{
    static string NewFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TuneLab.InstallManifestTests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---- 格式 ----

    [Fact]
    public void WhatWasWrittenIsWhatComesBack()
    {
        string[] entries = ["TuneLab.exe", @"Resources\Manual\intro.png", "tunelab.cmd"];

        var parsed = InstallManifest.Parse(InstallManifest.Format(entries));

        Assert.NotNull(parsed);
        Assert.Equal(entries.OrderBy(e => e, StringComparer.OrdinalIgnoreCase), parsed);
    }

    // 清单是要被人读、被 diff 的：顺序不该随文件系统的枚举次序变，同一个文件也不该出现两次。
    [Fact]
    public void EntriesComeOutSortedAndWithoutDuplicates()
    {
        var parsed = InstallManifest.Parse(InstallManifest.Format(
            ["b.dll", "a.dll", "B.dll", "a/nested.dll"]));

        Assert.Equal(["a.dll", @"a\nested.dll", "b.dll"], parsed);
    }

    // 没有版本行 = 不是这个格式的文件。当成空清单会删不干净，当成"全删"会删过头，所以两者都不做：
    // 交回 null，让调用方走"没有清单"那条路。
    [Fact]
    public void SomethingThatIsNotAManifestIsNotReadAsOne()
    {
        Assert.Null(InstallManifest.Parse("TuneLab.exe\nsomething.dll\n"));
        Assert.Null(InstallManifest.Parse(""));
    }

    // 读到更高的版本就整份不解释：这是一张删除清单，"大概能看懂"不够——新版本可能给某些行赋了别的
    // 含义，照旧规矩删就是删错东西。
    [Fact]
    public void AManifestFromTheFutureIsNotGuessedAt()
    {
        Assert.Null(InstallManifest.Parse("version 99\nTuneLab.exe\n"));
        Assert.Null(InstallManifest.Parse("version what\nTuneLab.exe\n"));
    }

    // 清单只能描述安装目录之内的文件。越界的条目在删除时本来也匹配不上（要删哪些是清单与"枚举安装
    // 目录得到的相对路径"的交集），这里在格式层面再挡一道。
    [Fact]
    public void EntriesThatPointOutsideTheFolderAreDropped()
    {
        var parsed = InstallManifest.Parse(
            "version 1\n" + @"..\..\Windows\System32\kernel32.dll" + "\n" + @"C:\Windows\notepad.exe" + "\nTuneLab.exe\n");

        Assert.Equal(["TuneLab.exe"], parsed);
    }

    [Fact]
    public void CommentsAndBlankLinesAreNotFiles()
    {
        var parsed = InstallManifest.Parse("# a note\nversion 1\n\nTuneLab.exe\n");

        Assert.Equal(["TuneLab.exe"], parsed);
    }

    // ---- 删除范围 ----

    [Fact]
    public void OnlyTheFilesTheInstallerPutThereAreDeleted()
    {
        var plan = InstallManifest.Plan(
            manifest: ["TuneLab.exe", "tunelab.cmd"],
            onDisk: ["TuneLab.exe", "tunelab.cmd", "my-notes.txt", @"Projects\song.tlpx"]);

        Assert.Equal(["TuneLab.exe", "tunelab.cmd"], plan.Delete);
        Assert.Equal(["my-notes.txt", @"Projects\song.tlpx"], plan.Keep);
    }

    // 清单里没有的东西不是"漏了"，是别人的：只报出来，不删。
    [Fact]
    public void AFolderFullOfSomeoneElsesFilesLosesNothing()
    {
        var plan = InstallManifest.Plan(manifest: [], onDisk: ["taxes.xlsx", @"photos\cat.png"]);

        Assert.Empty(plan.Delete);
        Assert.Equal(["taxes.xlsx", @"photos\cat.png"], plan.Keep);
    }

    // 清单里有、磁盘上已经没有的，不算一回事——用户手动删过一部分也不该让卸载报错。
    [Fact]
    public void FilesAlreadyGoneAreNotMissed()
    {
        var plan = InstallManifest.Plan(manifest: ["TuneLab.exe", "gone.dll"], onDisk: ["TuneLab.exe"]);

        Assert.Equal(["TuneLab.exe"], plan.Delete);
        Assert.Empty(plan.Keep);
    }

    // NTFS 不区分大小写，路径分隔符两种写法都可能出现——认领是按文件认的，不是按字符串长相认的。
    [Fact]
    public void CaseAndSlashesDoNotChangeWhoOwnsAFile()
    {
        var plan = InstallManifest.Plan(
            manifest: ["Resources/Manual/intro.png"],
            onDisk: [@"resources\manual\INTRO.PNG"]);

        Assert.Single(plan.Delete);
        Assert.Empty(plan.Keep);
    }

    // ---- 没有清单时的退路 ----

    // 旧安装器装的那一份没有账。它当初的行为就是整目录删，只在"目录名就是产品名"时才照做——那种目录
    // 是安装器自己造出来的；用户随手指的目录不猜。
    [Fact]
    public void WithoutAManifestOnlyAFolderTheInstallerWouldHaveMadeMayGo()
    {
        Assert.True(InstallManifest.MayDeleteWholeDirectoryWithoutManifest(@"C:\Users\me\AppData\Local\Programs\TuneLab"));
        Assert.True(InstallManifest.MayDeleteWholeDirectoryWithoutManifest(@"D:\stuff\tunelab\"));
        Assert.False(InstallManifest.MayDeleteWholeDirectoryWithoutManifest(@"D:\Apps"));
        Assert.False(InstallManifest.MayDeleteWholeDirectoryWithoutManifest(@"D:\Apps\TuneLab 2.1"));
    }

    // ---- 装完之后认领哪些文件 ----

    // 载荷之外，安装器自己还往目录里写了两个文件（tunelab.cmd 与清单本身）。漏认领就删不掉：
    // 清单留在原地会把本该消失的目录撑着，tunelab.cmd 则变成一个指向空处的入口。
    [Fact]
    public void TheManifestClaimsThePayloadPlusWhatTheInstallerWroteItself()
    {
        var dir = NewFolder();
        try
        {
            var claimed = Installer.CollectInstalledFiles(
                new FakePayload(["TuneLab.exe", @"runtimes\native.dll"]), dir, commandLineWritten: true).ToList();

            Assert.Contains("TuneLab.exe", claimed);
            Assert.Contains(@"runtimes\native.dll", claimed);
            Assert.Contains("tunelab.cmd", claimed);
            Assert.Contains(InstallManifest.FileName, claimed);
        }
        finally { Directory.Delete(dir, true); }
    }

    // 命令行本体没铺上时 tunelab.cmd 根本没写（见 CommandLineEntry），别认领一个不存在的文件。
    [Fact]
    public void AFileTheInstallerDidNotWriteIsNotClaimed()
    {
        var dir = NewFolder();
        try
        {
            var claimed = Installer.CollectInstalledFiles(
                new FakePayload(["TuneLab.exe"]), dir, commandLineWritten: false).ToList();

            Assert.DoesNotContain("tunelab.cmd", claimed);
        }
        finally { Directory.Delete(dir, true); }
    }

    // 更新只覆盖文件、不清理旧版本留下的那些。它们仍然是安装器装的——不并进新账，一次更新就能把它们
    // 变成"来路不明、不敢删"的东西，卸载完永远留在目录里。
    [Fact]
    public void AnUpdateStillClaimsWhatTheVersionBeforeItInstalled()
    {
        var dir = NewFolder();
        try
        {
            InstallManifest.Write(dir, ["TuneLab.exe", "dropped-in-2.1.dll", InstallManifest.FileName]);

            var claimed = Installer.CollectInstalledFiles(
                new FakePayload(["TuneLab.exe"]), dir, commandLineWritten: true).ToList();

            Assert.Contains("dropped-in-2.1.dll", claimed);
        }
        finally { Directory.Delete(dir, true); }
    }

    sealed class FakePayload(string[] entries) : IPayloadProvider
    {
        public long UncompressedSize => entries.Length;
        public IEnumerable<string> EnumerateEntries() => entries;
        public void ExtractTo(string targetDir, IProgress<ExtractProgress>? progress, CancellationToken ct) { }
    }

    // ---- 落盘 ----

    [Fact]
    public void TheManifestSurvivesARoundTripThroughTheInstallFolder()
    {
        var dir = NewFolder();
        try
        {
            Assert.Null(InstallManifest.Read(dir)); // 还没装，就没有账

            InstallManifest.Write(dir, ["TuneLab.exe", InstallManifest.FileName]);

            Assert.Equal([InstallManifest.FileName, "TuneLab.exe"], InstallManifest.Read(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
