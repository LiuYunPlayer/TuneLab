using System.Collections.Generic;
using System.IO;
using TuneLab.Extensions;
using TuneLab.Extensions.Scripted;
using Xunit;

namespace TuneLab.Tests;

// 脚本条目加载门（M0）的裁决矩阵（设计文档 §2/§8）：结构性错误 Failed、能力缺失/未实现 Skipped，
// 一切文案必须是作者看得懂的人话。
public class ScriptedEntryGateTests
{
    readonly string mPkg = Path.Combine(Path.GetTempPath(), "tl-scripted-gate-" + Path.GetRandomFileName());

    ExtensionInfo Entry(string runtime, string? entry = "plugin/main.py", string? deps = null,
        string? cls = null, string? assembly = null)
    {
        Directory.CreateDirectory(Path.Combine(mPkg, "plugin"));
        if (entry != null)
            File.WriteAllText(Path.Combine(mPkg, entry.Replace('/', Path.DirectorySeparatorChar)), "");
        return new ExtensionInfo { type = "format", runtime = runtime, entry = entry, deps = deps, entryClass = cls, assembly = assembly };
    }

    static RuntimeLocator EmptyLocator()
        => new(new FakeProber(), () => "", () => "", () => "", _ => null, () => Path.GetTempPath());

    [Fact]
    public void MissingEntryFileIsFailed()
    {
        var ext = Entry("python>=3.10");
        ext.entry = null;
        Assert.Equal(ScriptedGateStatus.Failed, ScriptedEntryGate.Evaluate(ext, mPkg, EmptyLocator()).Status);
        Assert.Contains("'entry'", ScriptedEntryGate.Evaluate(ext, mPkg, EmptyLocator()).Message);
    }

    [Fact]
    public void MixingAlcAndScriptedDeclarationsIsFailed()
    {
        var r = ScriptedEntryGate.Evaluate(Entry("python>=3.10", cls: "X.Y", assembly: "a.dll"), mPkg, EmptyLocator());
        Assert.Equal(ScriptedGateStatus.Failed, r.Status);
        Assert.Contains("pick one", r.Message);
    }

    [Fact]
    public void EntryEscapingPackageIsFailed()
    {
        var r = ScriptedEntryGate.Evaluate(Entry("python>=3.10", entry: "../outside.py"), mPkg, EmptyLocator());
        Assert.Equal(ScriptedGateStatus.Failed, r.Status);
        Assert.Contains("inside the package folder", r.Message);
    }

    [Fact]
    public void EntryFileAbsentOnDiskIsFailed()
    {
        var ext = Entry("python>=3.10");
        File.Delete(Path.Combine(mPkg, "plugin", "main.py"));
        var r = ScriptedEntryGate.Evaluate(ext, mPkg, EmptyLocator());
        Assert.Equal(ScriptedGateStatus.Failed, r.Status);
        Assert.Contains("not found", r.Message);
    }

    [Fact]
    public void PipDepsModeIsSkippedNotFailed()
    {
        var r = ScriptedEntryGate.Evaluate(Entry("python>=3.10", deps: "pip"), mPkg, EmptyLocator());
        Assert.Equal(ScriptedGateStatus.Skipped, r.Status);
        Assert.Contains("not implemented", r.Message);
    }

    [Fact]
    public void UnknownDepsModeIsFailed()
    {
        var r = ScriptedEntryGate.Evaluate(Entry("python>=3.10", deps: "apt"), mPkg, EmptyLocator());
        Assert.Equal(ScriptedGateStatus.Failed, r.Status);
    }

    [Fact]
    public void MalformedRuntimeSpecIsFailed()
    {
        var r = ScriptedEntryGate.Evaluate(Entry("ruby>=3"), mPkg, EmptyLocator());
        Assert.Equal(ScriptedGateStatus.Failed, r.Status);
        Assert.Contains("malformed 'runtime'", r.Message);
    }

    [Fact]
    public void RuntimeNotFoundIsSkippedWithActionableTrail()
    {
        var r = ScriptedEntryGate.Evaluate(Entry("python>=99.0"), mPkg, EmptyLocator());
        Assert.Equal(ScriptedGateStatus.Skipped, r.Status);
        Assert.Contains("no matching runtime", r.Message);
        Assert.Contains("install a 'runtime' package", r.Message.ToLowerInvariant());
        Assert.True(r.RuntimeMissing);                       // 自动部署只应答这一种 Skipped
        Assert.NotNull(r.Spec);
    }

    [Fact]
    public void ResolvedRuntimeYieldsReadyWithResolutionInMessage()
    {
        Directory.CreateDirectory(mPkg);
        var prober = new FakeProber();
        var exe = FakeProber.FakeExe(mPkg, "python.exe", prober, "python", "3.12.7");
        var locator = new RuntimeLocator(prober, () => exe.Exe, () => "", () => "", _ => null, () => mPkg);
        var r = ScriptedEntryGate.Evaluate(Entry("python>=3.10"), mPkg, locator);
        Assert.Equal(ScriptedGateStatus.Ready, r.Status);
        Assert.Equal("python", r.Runtime.Flavor);
        Assert.Equal("3.12.7", r.Runtime.Version);
        Assert.Contains("3.12.7", r.Message);
        Assert.False(r.RuntimeMissing);
    }
}
