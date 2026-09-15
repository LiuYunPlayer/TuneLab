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

// M3c 垂直切片：脚本 voice 引擎全链——catalog/声明五件套/continuation/peek 缓存/synthesize
// （反向 notes→snapshot→evaluate→segment 写回→四路产物发布）/事件推送转发。
// 对端是假 sidecar 的 voice-sine 靶形。
[Collection("SidecarSpawns")]
public class ScriptedVoiceTests
{
    static (SidecarHost host, RemoteVoiceEngine engine) MakeEngine()
    {
        var dll = ScriptedSidecarHostTests.FakeDllPath;
        var host = new SidecarHost("com.test.voice", Path.GetDirectoryName(dll)!,
            new RuntimeInfo(ScriptedSidecarHostTests.Dotnet, "dotnet-fake", "0.0", "x64"), Path.GetFileName(dll),
            callTimeout: TimeSpan.FromSeconds(10), heartbeat: TimeSpan.FromSeconds(60))
        {
            ExtraEnvironment = new() { ["TUNELAB_FAKE_MODE"] = "voice-sine" },
        };
        host.ExpectEngine("voice", "FakeSine");
        return (host, new RemoteVoiceEngine(host, "FakeSine", hasSettings: false));
    }

    [Fact(Timeout = 90_000)]
    public async Task VoiceEngineFullSurfaceRoundTrip()
    {
        var (host, engine) = MakeEngine();
        IVoiceSynthesisSession? session = null;
        var ctx = new VoiceTestContext(
            new VoiceTestNote("n0", 0.0, 0.5, 60, "la"),
            new VoiceTestNote("n1", 0.5, 1.0, 64, "du"));
        try
        {
            engine.Init();

            // —— catalog / layout（Init 期拉取、属性读缓存，"永不阻塞"契约）——
            Assert.Equal("sine", Assert.Single(engine.VoiceSourceInfos.Keys));
            Assert.Equal("Sine Bank", engine.VoiceSourceInfos["sine"].Name);
            var layoutItem = Assert.Single(engine.VoiceSourceLayout);
            Assert.Equal("sine", Assert.IsType<VoiceSourceLayoutVoice>(layoutItem).VoiceId);

            // —— 声明五件套（config wire 重建）——
            var autos = engine.GetAutomationConfigs(new VoiceTestPartContext(ctx));
            Assert.Equal("VR", autos.Keys.First().DisplayText);
            Assert.Equal(0.5, autos["vibrato_rate".AsKey()].DefaultValue);
            Assert.True(engine.GetSynthesizedParameterConfigs(new VoiceTestPartContext(ctx))["energy".AsKey()].IsPiecewise);
            var partCfg = Assert.IsType<ObjectConfig>(engine.GetPartPropertyConfig(new VoiceTestPartContext(ctx)));
            Assert.IsType<SliderConfig>(partCfg.Properties["tension"]);
            var noteCtx = new VoiceTestNoteContext(ctx, ctx.Notes.First()!);
            Assert.Single(((ObjectConfig)engine.GetNotePropertyConfig(noteCtx)).Properties);
            Assert.Empty(engine.GetPhonemePropertyConfigs(noteCtx));

            // —— 会话 ——
            session = engine.CreateSession(ctx);
            Assert.Equal("la", session.DefaultLyric);
            Assert.False(session.IsContinuation(ctx.Notes.First()!));

            // peek：首拍有段、二拍空（fake 计数），并验证缓存随 dirty 推进重问
            var first = session.GetNextPendingSynthesisRange(0, 2) ?? throw new Exception("expected pending range");
            Assert.Equal(0.0, first.StartTime);
            Assert.Equal(0.5, first.EndTime);
            Assert.Null(session.GetNextPendingSynthesisRange(0, 2));          // 二拍：fake 已无 pending

            // synthesize：反向链全活 + 四路产物
            await session.SynthesizeNext(0, 0.5, CancellationToken.None);

            var seg = Assert.Single(ctx.Segments);
            Assert.True(seg.Committed);
            Assert.Equal(22050, seg.Written.Count);                            // 0.5s × 44100
            Assert.Equal(0f, seg.Written[0], 5);
            Assert.True(Math.Abs(seg.Written[50] - 0.25f * MathF.Sin(2 * MathF.PI * 440f * 50 / 44100f)) < 1e-4);
            var pitch = session.SynthesizedPitch;
            Assert.Single(pitch.Segments);
            Assert.Equal(2, pitch.Segments[0].Count);
            Assert.Equal(62.0, pitch.Segments[0][1].Y);
            var syll = session.SynthesizedPhonemes["n0"];                      // 归属回指 = snapshot request_id
            Assert.Equal("a", Assert.Single(syll.BodyPhonemes).Symbol);
            Assert.Equal(1.0, syll.BodyPhonemes[0].StretchWeight);
            var st = Assert.Single(session.Status);
            Assert.Equal(SynthesisSegmentStatus.Synthesized, st.Status);
            Assert.Single(session.SynthesizedParameters["energy"].Segments);

            // 事件推送转发：context 变更 → dirtySeq 推进 → peek 重问（fake 计数已尽 → null 也证明问了）
            ctx.TriggerPitchRange(0, 1);
            Assert.Null(session.GetNextPendingSynthesisRange(0, 2));
        }
        finally
        {
            session?.Dispose();
            host.Shutdown();
        }
        Assert.True(ctx.Segments[0].Disposed);   // 会话终结收割未提交的段句柄
    }

    sealed class VoiceTestPartContext : IVoiceSynthesisPartPropertyContext
    {
        public VoiceTestPartContext(VoiceTestContext ctx) => Parts = [new VoiceTestPartView(ctx)];
        public System.Collections.Generic.IReadOnlyList<IVoiceSynthesisPartView> Parts { get; }
    }

    sealed class VoiceTestNoteContext : IVoiceSynthesisNotePropertyContext
    {
        public VoiceTestNoteContext(VoiceTestContext ctx, IVoiceSynthesisNote note)
        {
            Part = new VoiceTestPartView(ctx);
            Notes = [new VoiceTestNoteView(note)];
        }
        public IVoiceSynthesisPartView Part { get; }
        public System.Collections.Generic.IReadOnlyList<IVoiceSynthesisNoteView> Notes { get; }
    }

    sealed class VoiceTestPartView : IVoiceSynthesisPartView
    {
        public VoiceTestPartView(VoiceTestContext ctx) { _ctx = ctx; }
        readonly VoiceTestContext _ctx;
        public string VoiceId => _ctx.VoiceId;
        public System.Collections.Generic.IReadOnlyList<IVoiceSynthesisNoteView> Notes
            => _ctx.Notes.Select(n => (IVoiceSynthesisNoteView)new VoiceTestNoteView(n)).ToList();
        public PropertyObject PartProperties => PropertyObject.Empty;
        public IReadOnlyMap<string, IAutomationEvaluator> Automations { get; } = new Map<string, IAutomationEvaluator>();
    }

    sealed class VoiceTestNoteView : IVoiceSynthesisNoteView
    {
        public VoiceTestNoteView(IVoiceSynthesisNote n) { _n = n; }
        readonly IVoiceSynthesisNote _n;
        public double StartTime => _n.StartTime.Value;
        public double EndTime => _n.EndTime.Value;
        public int Pitch => _n.Pitch.Value;
        public string Lyric => _n.Lyric.Value;
        public PropertyObject Properties => PropertyObject.Empty;
        public double BodyOffset => _n.BodyOffset.Value;
        public System.Collections.Generic.IReadOnlyList<IVoiceSynthesisPhonemeView> LeadingPhonemes => System.Array.Empty<IVoiceSynthesisPhonemeView>();
        public System.Collections.Generic.IReadOnlyList<IVoiceSynthesisPhonemeView> BodyPhonemes => System.Array.Empty<IVoiceSynthesisPhonemeView>();
    }
}
