using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions.Scripted;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// M2b 垂直切片：脚本 effect 引擎的完整往返——正向声明/生命周期 + 反向数据线程调用
// （input_read / properties / evaluate / segment 族）+ 发布（status/parameters）。
// 对端是假 sidecar 的 effect-gain 靶形（读入 → ×amount → 写回 + 回显 + 状态声称）。
public class ScriptedEffectTests
{
    static SidecarHost MakeHost(string engineId)
    {
        var dll = ScriptedSidecarHostTests.FakeDllPath;
        var host = new SidecarHost("com.test." + engineId, Path.GetDirectoryName(dll)!,
            new RuntimeInfo(ScriptedSidecarHostTests.Dotnet, "dotnet-fake", "0.0", "x64"), Path.GetFileName(dll),
            callTimeout: TimeSpan.FromSeconds(10), heartbeat: TimeSpan.FromSeconds(60))
        {
            ExtraEnvironment = new() { ["TUNELAB_FAKE_MODE"] = "effect-gain" },
        };
        host.ExpectEngine("effect", engineId);
        return host;
    }

    [Fact(Timeout = 60_000)]
    public async Task GainEffectRoundTripThroughReverseCalls()
    {
        var host = MakeHost("FakeGain");
        var engine = new RemoteEffectEngine(host, "FakeGain", hasSettings: false);
        var ctx = new EffectTestContext();
        IEffectSynthesisSession? session = null;
        try
        {
            engine.Init();

            // 声明面（缓存 RPC + config wire 重建）
            var propCfg = Assert.IsType<ObjectConfig>(engine.GetPropertyConfig(new EffectTestPropContext()));
            Assert.IsType<SliderConfig>(propCfg.Properties["amount"]);
            var autos = engine.GetAutomationConfigs(new EffectTestPropContext());
            Assert.Equal("Intensity", autos.Keys.First().DisplayText);
            Assert.Equal(0.5, autos["intensity".AsKey()].DefaultValue);
            var synth = engine.GetSynthesizedParameterConfigs(new EffectTestPropContext());
            Assert.True(synth["loudness".AsKey()].IsPiecewise);

            // 处理（反向链全活：读入→取参数→建段→写→commit→回显→状态）
            session = engine.CreateSession(ctx);
            await session.Process(CancellationToken.None);

            Assert.Single(ctx.Segments);
            var seg = ctx.Segments[0];
            Assert.True(seg.Committed);
            Assert.Equal(4410, seg.Written.Count);
            for (int i = 0; i < seg.Written.Count; i++)          // fake 按 ×0.5（properties 喂回 amount=0.5）
                Assert.Equal(ctx.Input.Buffer[i] * 0.5f, seg.Written[i], 6);
            var loud = session.SynthesizedParameters["loudness"];
            Assert.Single(loud.Segments);
            Assert.Equal(2, loud.Segments[0].Count);
            var status = session.Status;
            Assert.Single(status);
            Assert.Equal(SynthesisSegmentStatus.Synthesized, status[0].Status);
            Assert.Equal(1.0, status[0].Progress);
            session.Dispose();
            Assert.True(seg.Disposed);                           // 会话销毁带走段句柄
        }
        finally
        {
            host.Shutdown();
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task EvaluateReversePullsAutomationValues()
    {
        var host = MakeHost("FakeEval");
        var engine = new RemoteEffectEngine(host, "FakeEval", hasSettings: false);
        var ctx = new EffectTestContext();
        try
        {
            engine.Init();
            var session = engine.CreateSession(ctx);
            await session.Process(CancellationToken.None);
            var auto = (EffectTestAutomation)ctx.Automations["intensity"];
            Assert.True(auto.Touched);                           // 反向曲线求值命中了活视图
        }
        finally
        {
            host.Shutdown();
        }
    }
}

internal static class StringKeyExt
{
    public static PropertyKey AsKey(this string s) => new(s);
}
