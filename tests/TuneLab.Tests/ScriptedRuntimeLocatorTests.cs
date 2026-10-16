using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TuneLab.Extensions.Scripted;
using Xunit;

namespace TuneLab.Tests;

// 四级探测（settings → env → runtime 资源包 → PATH）+ 真跑核验 + 缓存（设计文档 §8.1/§8.2）。
// 全程 FakeProber / 假扩展根；不碰真解释器与真 %APPDATA%。
public class ScriptedRuntimeLocatorTests
{
    readonly string mRoot = Path.Combine(Path.GetTempPath(), "tl-scripted-locator-" + Path.GetRandomFileName());

    static RuntimeLocator Make(FakeProber prober, string settingsPython = "", string settingsNode = "", string settingsLua = "",
        IReadOnlyDictionary<string, string?>? env = null, string extensionsRoot = "")
        => new(prober, () => settingsPython, () => settingsNode, () => settingsLua,
            name => env != null && env.TryGetValue(name, out var v) ? v : null,
            () => extensionsRoot);

    [Fact]
    public void SettingsBeatsEnv()
    {
        Directory.CreateDirectory(mRoot);
        var prober = new FakeProber();
        var fromSettings = FakeProber.FakeExe(mRoot, "pyA.exe", prober, "python", "3.11.0");
        var fromEnv = FakeProber.FakeExe(mRoot, "pyB.exe", prober, "python", "3.12.7");
        Assert.True(RuntimeSpec.TryParse("python>=3.10", out var spec, out _));
        var locator = Make(prober, settingsPython: fromSettings.Exe,
            env: new Dictionary<string, string?> { [RuntimeLocator.EnvPython] = fromEnv.Exe });

        Assert.True(locator.TryResolve(spec, out var info, out _));
        Assert.Equal(fromSettings.Exe, info.Exe);
        Assert.Equal(1, prober.ProbeCount);
    }

    [Fact]
    public void SpoofedCandidateFallsThroughToNext()
    {
        Directory.CreateDirectory(mRoot);
        var prober = new FakeProber();
        var good = FakeProber.FakeExe(mRoot, "node_real.exe", prober, "node", "20.11.0");
        var stub = Path.Combine(mRoot, "node_stub.exe");
        File.WriteAllText(stub, "microsoft store redirect stub");   // 在盘上，但表里没有 → 真跑核验 9009
        var locator = Make(prober, settingsNode: stub,
            env: new Dictionary<string, string?> { [RuntimeLocator.EnvNode] = good.Exe });
        Assert.True(locator.TryResolve(TryParseOrThrow("node>=18"), out var info, out var trail));
        Assert.Equal(good.Exe, info.Exe);
        Assert.Contains("rejected", trail);
        Assert.Contains("9009", trail);
    }

    [Fact]
    public void PackagesAreOrderedNewestFirst()
    {
        Directory.CreateDirectory(mRoot);
        var exts = Path.Combine(mRoot, "exts");
        WriteRuntimePackage(Path.Combine(exts, "py-3112"), "a.py3112", "python", "3.11.2");
        WriteRuntimePackage(Path.Combine(exts, "py-3127"), "a.py3127", "python", "3.12.7");
        WriteRuntimePackage(Path.Combine(exts, "broken"), "a.broken", "python", null);
        var exes = RuntimeLocator.EnumerateRuntimePackageExes("python", exts);
        Assert.Collection(exes,
            e => Assert.Contains("py-3127", e),
            e => Assert.Contains("py-3112", e));
    }

    [Fact]
    public void ProbeResultsAreCachedByExePath()
    {
        Directory.CreateDirectory(mRoot);
        var prober = new FakeProber();
        var exe = FakeProber.FakeExe(mRoot, "lua54.exe", prober, "lua", "5.4.6");
        var locator = Make(prober, settingsLua: exe.Exe);
        var spec = TryParseOrThrow("lua>=5.4");
        Assert.True(locator.TryResolve(spec, out _, out _));
        Assert.True(locator.TryResolve(spec, out _, out _));
        Assert.Equal(1, prober.ProbeCount);
    }

    [Fact]
    public void UnsatisfiableRangeRejectsEveryCandidateAndTrailsWhy()
    {
        Directory.CreateDirectory(mRoot);
        var prober = new FakeProber();
        var exe = FakeProber.FakeExe(mRoot, "py.exe", prober, "python", "3.8.10");
        var locator = Make(prober, settingsPython: exe.Exe);
        Assert.False(locator.TryResolve(TryParseOrThrow("python>=3.10"), out _, out var trail));
        Assert.Contains("does not satisfy", trail);
    }

    static RuntimeSpec TryParseOrThrow(string s)
    {
        Assert.True(RuntimeSpec.TryParse(s, out var spec, out _));
        return spec;
    }

    static string WriteRuntimePackage(string dir, string id, string flavor, string? version)
    {
        Directory.CreateDirectory(dir);
        var manifest = new Dictionary<string, object?>
        {
            ["id"] = id, ["name"] = id, ["type"] = "runtime", ["flavor"] = flavor, ["version"] = "1.0.0",
        };
        if (version != null)
            manifest["runtime-version"] = version;
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        // 有 exe 才算可用包（ResolvePackageExe 探 runtime/<exe> 或 runtime/bin/<exe>）。
        var exeDir = Path.Combine(dir, "runtime");
        Directory.CreateDirectory(exeDir);
        File.WriteAllText(Path.Combine(exeDir, OperatingSystem.IsWindows() ? "python.exe" : "python"), "fake");
        return dir;
    }
}
