using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions.Scripted;
using Xunit;

namespace TuneLab.Tests;

// 真进程 + 真 stdio 帧协议的生命周期封条（设计文档 §12.0 M1）。对端是 .NET 假 sidecar
// （tests/ScriptedFakeSidecar），CI 无需任何解释器；测的是 SidecarHost 的握手/请求应答/
// provides 核对/崩溃重生 epoch/退避熔断/优雅关停。
[Collection("SidecarSpawns")]
    public class ScriptedSidecarHostTests
{
    static readonly string sFakeDir = ResolveFakeDir();
    static readonly string sFakeDll = Path.Combine(sFakeDir, "TuneLab.ScriptedFakeSidecar.dll");
    internal static string FakeDllPath => sFakeDll;
    internal static string Dotnet => sDotnet;
    static readonly string sDotnet = Process.GetCurrentProcess().ProcessName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? "dotnet"
        : FindDotnet();

    static string ResolveFakeDir()
    {
        // 从测试程序集目录向上找仓库根下的构建产物（tests/ 下两级）。
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var hit = Path.Combine(dir.FullName, "tests", "ScriptedFakeSidecar", "bin", "Debug", "net8.0", "TuneLab.ScriptedFakeSidecar.dll");
            if (File.Exists(hit))
                return Path.GetDirectoryName(hit)!;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("fake sidecar dll not built; run: dotnet build tests/ScriptedFakeSidecar -c Debug");
    }

    static string FindDotnet()
    {
        var onPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator).Select(d => Path.Combine(d, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
            .FirstOrDefault(File.Exists);
        return onPath ?? "dotnet";
    }

    static SidecarHost MakeHost(string mode, TimeSpan? callTimeout = null)
    {
        var runtime = new RuntimeInfo(Dotnet, "dotnet-fake", "0.0", "x64");
        var host = new SidecarHost("com.test.fake", sFakeDir, runtime, "TuneLab.ScriptedFakeSidecar.dll", callTimeout)
        {
            ExtraEnvironment = new() { ["TUNELAB_FAKE_MODE"] = mode },
        };
        host.ExpectFormat("TuneLab.ScriptedFakeSidecar.dll", ["tst"], []);
        return host;
    }

    static ScriptedSidecarHostTests()
    {
        Assert.True(File.Exists(sFakeDll), "fake sidecar must be built before these tests run");
    }

    [Fact]
    public async Task HandshakeSucceedsAndEpochIsOne()
    {
        var host = MakeHost("normal");
        try
        {
            await host.EnsureRunningAsync();
            Assert.Equal(1, host.Epoch);
        }
        catch (Exception ex)
        {
            Assert.Fail("handshake failed: " + ex + " || reader: " + host.LastReaderFault + " || stderr: " + host.StderrTail);
        }
        finally { host.Shutdown(); }
    }

    [Fact]
    public async Task FormatImportReturnsSyntheticProject()
    {
        var host = MakeHost("normal");
        try
        {
            var result = await host.CallAsync("format.import", new System.Text.Json.Nodes.JsonObject
            {
                ["entry"] = "TuneLab.ScriptedFakeSidecar.dll",
                ["stream_ref"] = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Path.GetRandomFileName()),
            });
            Assert.NotNull(result);
            Assert.NotNull(result!["project"]);
            Assert.NotNull(result["project"]!["tracks"]);
        }
        finally { host.Shutdown(); }
    }

    [Fact]
    public async Task HandshakeWithoutCapabilitiesKillsProcess()
    {
        var host = MakeHost("no-capability");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => host.EnsureRunningAsync());
            // 握手失败：不留活口（进程被杀），epoch 仍推进
            Assert.True(host.Epoch >= 1);
        }
        finally { host.Shutdown(); }
    }

    [Fact]
    public async Task CallTimesOutWhenPeerHangs()
    {
        var host = MakeHost("hang", TimeSpan.FromSeconds(3));
        try
        {
            // 握手用同一超时？不——握手固定 30s。hang 只挂后续调用，握手仍成功。
            await host.EnsureRunningAsync();
            var ex = await Assert.ThrowsAsync<SidecarRpcException>(() =>
                host.CallAsync("format.import", new System.Text.Json.Nodes.JsonObject { ["entry"] = "x", ["stream_ref"] = "y" }));
            Assert.Equal("timeout", ex.Code);
        }
        finally { host.Shutdown(); }
    }

    [Fact]
    public async Task CrashThenNextCallRespawnsWithBumpedEpoch()
    {
        var host = MakeHost("crash-on-import");
        try
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                host.CallAsync("format.import", new System.Text.Json.Nodes.JsonObject { ["entry"] = "x", ["stream_ref"] = "y" }));
            Assert.True(host.Epoch >= 1);

            // 重开：换回 normal 模式，下一次调用触发 EnsureRunning 重 spawn，epoch 递增。
            host.ExtraEnvironment!["TUNELAB_FAKE_MODE"] = "normal";
            host.ResetCircuit();
            var ok = await host.CallAsync("format.import", new System.Text.Json.Nodes.JsonObject
            {
                ["entry"] = "TuneLab.ScriptedFakeSidecar.dll",
                ["stream_ref"] = Path.GetTempPath(),
            });
            Assert.NotNull(ok?["project"]);
            Assert.True(host.Epoch >= 2);
        }
        finally { host.Shutdown(); }
    }

    [Fact]
    public async Task ShutdownIsIdempotentAndStopsProcess()
    {
        var host = MakeHost("normal");
        await host.EnsureRunningAsync();
        host.Shutdown();
        host.Shutdown();   // 幂等，不抛
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.EnsureRunningAsync());
    }

    [Fact(Timeout = 30_000)]
    public async Task AutoRestartLoopRewarmsAfterUnexpectedDeath()
    {
        // §3.2 重启环：常驻引擎声明 AutoRestart → 崩溃后按退避自动 warm 回来，
        // 无需下一次调用驱动（这里制造一次 crash-on-import，再等 Running 自己翻回 true）。
        var runtime = new RuntimeInfo(Dotnet, "dotnet-fake", "0.0", "x64");
        var host = new SidecarHost("com.test.rewarm", sFakeDir, runtime, "TuneLab.ScriptedFakeSidecar.dll",
            heartbeat: TimeSpan.FromSeconds(30))
        {
            AutoRestart = true,
            ExtraEnvironment = new() { ["TUNELAB_FAKE_MODE"] = "crash-on-import" },
        };
        host.ExpectFormat("TuneLab.ScriptedFakeSidecar.dll", ["tst"], []);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                host.CallAsync("format.import", System.Text.Json.Nodes.JsonNode.Parse("{\"entry\":\"x\",\"stream_ref\":\"y\"}")));
            Assert.False(host.Running);   // 刚死

            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline && !host.Running)
                await Task.Delay(150);
            Assert.True(host.Running);    // 退避后自己爬回来了（握手已过）
        }
        finally { host.Shutdown(); }
    }

    [Fact]
    public async Task HeartbeatKillsWedgedPeerAfterConsecutiveMisses()
    {
        // mute-ping：握手正常、之后吞掉一切 ping —— 正是"半死"形态（防线 3 的靶形）。
        var runtime = new RuntimeInfo(Dotnet, "dotnet-fake", "0.0", "x64");
        var host = new SidecarHost("com.test.wedge", sFakeDir, runtime, "TuneLab.ScriptedFakeSidecar.dll",
            heartbeat: TimeSpan.FromMilliseconds(200), pingTimeout: TimeSpan.FromMilliseconds(300))
        {
            ExtraEnvironment = new() { ["TUNELAB_FAKE_MODE"] = "mute-ping" },
        };
        host.ExpectFormat("TuneLab.ScriptedFakeSidecar.dll", ["tst"], []);
        var died = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Died += () => died.TrySetResult(true);
        try
        {
            await host.EnsureRunningAsync();
            Assert.True(host.Running);
            var winner = await Task.WhenAny(died.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(died.Task, winner);          // 4 连失联 → 杀树 → Died
            Assert.False(host.Running);
        }
        finally { host.Shutdown(); }
    }

    [Fact]
    public async Task PendingSettingsRideHandshake_AndLiveSettingsNotify()
    {
        var settingsLog = Path.Combine(Path.GetTempPath(), "tl-settings-" + Path.GetRandomFileName() + ".log");
        var runtime = new RuntimeInfo(Dotnet, "dotnet-fake", "0.0", "x64");
        var host = new SidecarHost("com.test.settings", sFakeDir, runtime, "TuneLab.ScriptedFakeSidecar.dll")
        {
            ExtraEnvironment = new() { ["TUNELAB_FAKE_MODE"] = "normal", ["TUNELAB_FAKE_SETTINGS_LOG"] = settingsLog },
        };
        host.ExpectFormat("TuneLab.ScriptedFakeSidecar.dll", ["tst"], []);
        // spawn 前灌值 → 必须随 initialize 补发（ApplyPersisted 天然早于任何调用）。
        host.SetSettings("TuneLab.ScriptedFakeSidecar.dll", System.Text.Json.Nodes.JsonNode.Parse("""{"model":{"s":"m1"}}"""));
        try
        {
            await host.EnsureRunningAsync();
            // 握手补发 = 原样转发的 tagged 形（解码发生在 façade 侧，见 Python ServiceTests）。
            Assert.Contains("handshake\t{\"model\":{\"s\":\"m1\"}}", File.ReadAllText(settingsLog));

            // 运行中灌值 → 即时 settings.apply 通知。
            host.SetSettings("TuneLab.ScriptedFakeSidecar.dll", System.Text.Json.Nodes.JsonNode.Parse("""{"model":{"s":"m2"}}"""));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && !File.ReadAllText(settingsLog).Contains("notify\t{\"model\":{\"s\":\"m2\"}}"))
                await Task.Delay(50);
            Assert.Contains("notify\t{\"model\":{\"s\":\"m2\"}}", File.ReadAllText(settingsLog));
        }
        finally
        {
            host.Shutdown();
            TryDelete(settingsLog);
        }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
