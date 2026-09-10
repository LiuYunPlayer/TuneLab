using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using TuneLab.Setup.Core;
using Xunit;

namespace TuneLab.Tests;

// 卸载删文件这一步，在真的临时目录上真跑一遍。
//
// 【为什么不满足于纯函数】SetupInstallManifestTests 钉的是"该删哪些"的判据；这里钉的是"照判据动手之后
// 磁盘变成什么样"。删错文件拿不回来，而出错的地方恰恰在两者之间——空目录清理会不会顺手把整棵树端了、
// 用户自己放的东西会不会被连坐、目录该不该消失。这些只有在文件系统上看才算数。
//
// Uninstaller.Run 会动 HKCU 与开始菜单，测试里跑不得，所以只驱动它的两个接缝：定范围 + 删文件。
[SupportedOSPlatform("windows")]
public class SetupUninstallTests : IDisposable
{
    readonly string mDir;
    readonly List<string> mReport = [];

    public SetupUninstallTests()
    {
        mDir = Path.Combine(Path.GetTempPath(), "TuneLab.UninstallTests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(mDir))
            Directory.Delete(mDir, true);
        GC.SuppressFinalize(this);
    }

    // 在 mDir 下面（或它的兄弟目录 name 下面）造一份"装好的样子"。
    string Place(string root, params string[] relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var full = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
        }
        return root;
    }

    void Uninstall(string dir)
    {
        var plan = Uninstaller.PlanDeletion(dir, out var refusal);
        if (refusal != null)
            mReport.Add(refusal);
        Uninstaller.DeleteInstalledFiles(dir, plan, mReport.Add);
    }

    static List<string> Remaining(string dir)
        => Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f)).OrderBy(f => f, StringComparer.Ordinal).ToList()
            : [];

    // 这一条就是那个 bug：`-silent -dir D:\Apps` 把一个本来有东西的目录当成安装目录，
    // 卸载曾经对它 Directory.Delete(recursive) —— D:\Apps 整个没了。现在只删记过账的。
    [Fact]
    public void UninstallingFromAFolderTheUserAlsoUsesTakesOnlyOurFiles()
    {
        Place(mDir, "TuneLab.exe", @"runtimes\win\native.dll", "tunelab.cmd");
        InstallManifest.Write(mDir, ["TuneLab.exe", @"runtimes\win\native.dll", "tunelab.cmd", InstallManifest.FileName]);
        Place(mDir, "taxes.xlsx", @"my songs\demo.tlpx");

        Uninstall(mDir);

        Assert.True(Directory.Exists(mDir)); // 目录里还有人家的东西，不该消失
        Assert.Equal([@"my songs\demo.tlpx", "taxes.xlsx"], Remaining(mDir));
        Assert.False(Directory.Exists(Path.Combine(mDir, "runtimes"))); // 空了就清掉，不留空壳
        Assert.Contains(mReport, line => line.Contains("were not installed by TuneLab"));
    }

    // 独占一个目录的正常安装：删完什么都不剩，目录本身也该消失——用户看到的是"卸载干净了"。
    [Fact]
    public void AFolderThatHeldNothingButTuneLabDisappears()
    {
        Place(mDir, "TuneLab.exe", @"Resources\Manual\intro.png");
        InstallManifest.Write(mDir, ["TuneLab.exe", @"Resources\Manual\intro.png", InstallManifest.FileName]);

        Uninstall(mDir);

        Assert.False(Directory.Exists(mDir));
        Assert.Contains(mReport, line => line.StartsWith("Removed \"", StringComparison.Ordinal));
    }

    // 旧安装器装的那份没有账。目录名就是产品名 = 安装器自己造的目录，照它当初的行为整个删掉。
    [Fact]
    public void AnOldInstallInAFolderTheInstallerMadeIsStillCleanedUp()
    {
        var legacy = Place(Path.Combine(mDir, "TuneLab"), "TuneLab.exe", @"sub\thing.dll");

        Uninstall(legacy);

        Assert.False(Directory.Exists(legacy));
    }

    // 没有账，目录也不是我们造的名字：一个文件都不动，并说清为什么。
    // 猜错的代价是删掉用户的东西，猜对的收益只是省他一次手动删除。
    [Fact]
    public void WithoutAManifestAFolderWeDidNotNameIsLeftUntouched()
    {
        var strangers = Place(Path.Combine(mDir, "Apps"), "TuneLab.exe", "taxes.xlsx");

        Uninstall(strangers);

        Assert.Equal(["TuneLab.exe", "taxes.xlsx"], Remaining(strangers));
        Assert.Contains(mReport, line => line.Contains("no install manifest"));
    }

    // 用户先手动删了安装目录，再去"添加或删除程序"点卸载：不该炸，登记那边照拆（这里只看没抛）。
    [Fact]
    public void AFolderThatIsAlreadyGoneIsNotAProblem()
    {
        Uninstall(Path.Combine(mDir, "not-here"));

        Assert.Empty(Remaining(Path.Combine(mDir, "not-here")));
    }

    // 清单里有、磁盘上早没了的条目不该让卸载停下——用户可能自己删过一部分。
    [Fact]
    public void EntriesThatAreAlreadyGoneDoNotStopTheRest()
    {
        Place(mDir, "TuneLab.exe");
        InstallManifest.Write(mDir, ["TuneLab.exe", "deleted-by-hand.dll", InstallManifest.FileName]);

        Uninstall(mDir);

        Assert.False(Directory.Exists(mDir));
    }

    // 卸载器必须能在「身边只有它自己那几个文件」的目录里跑起来。
    //
    // 【这条钉的是一次真出过的事故】卸载要删的目录里就有卸载器自己，所以它先把自己搬到临时目录再从
    // 那里删（Uninstaller.RelocateSelf）。搬过去的只有 TuneLab.Setup.* 那几个文件——Avalonia 的 dll
    // 不在旁边。而 JIT 编译一个方法时会解析这个方法体里提到的全部类型，不管那几行执行不执行到：
    // Program.Main 里只要还提着一个 Avalonia 类型，副本就在进 Main 之前抛 FileNotFoundException。
    // 它是 GUI 子系统的进程，这一抛没有窗口、没有控制台、日志里也没有一行——用户点了卸载，什么都
    // 不会发生，注册表登记和整个安装目录原封不动。已发布的 2.0.x 就是这样，卸载一直是空操作。
    //
    // 复现代价高（要打出安装包、真装一遍），而回归是静默的，所以在这里用最小的等价条件钉住：
    // 把 TuneLab.Setup.* 单独复制到一个空目录，跑 -help（不碰注册表、不碰文件），起得来就算过。
    // 它覆盖的是 Main 这一层；卸载路径再往下（SilentRunner.Run → Uninstaller）只依赖 Core，
    // 那几个类不引 Avalonia。
    [Fact]
    public void TheUninstallerRunsWithNothingButItsOwnFilesBesideIt()
    {
        var exeInTestOutput = Path.Combine(AppContext.BaseDirectory, "TuneLab.Setup.exe");
        Assert.True(File.Exists(exeInTestOutput), "TuneLab.Setup.exe is missing from the test output.");

        var isolated = Path.Combine(mDir, "isolated");
        Directory.CreateDirectory(isolated);
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "TuneLab.Setup.*"))
            File.Copy(file, Path.Combine(isolated, Path.GetFileName(file)));

        using var process = Process.Start(new ProcessStartInfo(Path.Combine(isolated, "TuneLab.Setup.exe"), "-help")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "The isolated installer did not exit.");

        // 起不来时 stderr 里是那句 "Could not load file or assembly 'Avalonia.…'"，一并抛出来，
        // 免得只看到一个退出码还要再查一遍。
        Assert.True(process.ExitCode == 0, $"exit {process.ExitCode}; stderr: {error}");
        Assert.Contains("TuneLab installer.", output);
    }
}
