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
}
