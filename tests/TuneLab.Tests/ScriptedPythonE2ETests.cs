using System;
using System.IO;
using System.Linq;
using System.Text;
using TuneLab.Extensions;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Scripted;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 端到端真解释器集成（设计 §12.0 M1 验收：HeadlessHost E2E 装→导入→导出）。
// 用 SidecarHost/RemoteFormat 而非完整 FormatsManager，是为了不碰 %APPDATA% 的已装扩展目录。
// python 不在场即 vacuously pass（CI 由 needs-interpreter 任务真跑）。trait 标记，
// 单独运行： dotnet test --filter "runtime=needs-interpreter"
[Trait("runtime", "needs-interpreter")]
[Collection("SidecarSpawns")]
    public class ScriptedPythonE2ETests
{
    static readonly string sPackageDir = LocateUp("tests", "plugins", "PyFormat");

    static string LocateUp(params string[] tail)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(tail).ToArray());
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("PyFormat package not found: " + Path.Combine(tail));
    }

    [Fact]
    public void RealPythonImportExportRoundTrip()
    {
        if (!RuntimeLocator.Default.TryResolve(ParseSpec("python>=3.10"), out var python, out var trail))
        {
            if (Environment.GetEnvironmentVariable("TUNELAB_REQUIRE_PYTHON") == "1")
                Assert.Fail("python not resolved: " + trail);
            return;   // 无解释器环境：vacuous pass；真 CI 装了 python 才验
        }

        var facadeDir = LocateUp("scripting", "python");
        Environment.SetEnvironmentVariable("PYTHONPATH", facadeDir);   // 子进程继承

        var entry = "plugin/main.py";
        var host = SidecarRegistry.For("com.tunelab.test.pyformat", sPackageDir, python, entry);
        host.ExpectFormat(entry, ["pyt"], ["pyt"]);
        var proxy = new RemoteFormat(host, entry);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // 导入：一行一音符的文本 → ProjectInfo
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("60 0 480 la\n62 480 240 du\n"));
            var project = ((IImportFormat)proxy).Deserialize(input);
            Assert.Single(project.Tracks);
            var part = Assert.IsType<MidiPartInfo>(project.Tracks[0].Parts[0]);
            Assert.Equal(2, part.Notes.Count);
            Assert.Equal(60, part.Notes[0].Pitch);
            Assert.Equal("la", part.Notes[0].Lyric);
            Assert.Equal("pytxt", part.Notes[0].Properties.GetString("source"));   // 跨语言 tagged property 往返

            // 导出：ProjectInfo → 文本
            using var output = new MemoryStream();
            ((IExportFormat)proxy).Serialize(output, project);
            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.Contains("60 0 480 la", text);
            Assert.Contains("62 480 240 du", text);
        }
        finally
        {
            host.Shutdown();
        }
        // 真子进程往返（spawn + 两次 RPC）不可能亚百毫秒——这条把"vacuous 早退"钉死。
        Assert.True(sw.ElapsedMilliseconds > 50, $"round-trip suspiciously fast: {sw.ElapsedMilliseconds}ms");
    }

    static RuntimeSpec ParseSpec(string s)
    {
        Assert.True(RuntimeSpec.TryParse(s, out var spec, out _));
        return spec;
    }

    [Fact(Timeout = 120_000)]
    public void RealPythonVoiceSynthesisRoundTrip()
    {
        if (!RuntimeLocator.Default.TryResolve(ParseSpec("python>=3.10"), out var python, out var trail))
        {
            if (Environment.GetEnvironmentVariable("TUNELAB_REQUIRE_PYTHON") == "1")
                Assert.Fail("python not resolved: " + trail);
            return;
        }
        Environment.SetEnvironmentVariable("PYTHONPATH", LocateUp("scripting", "python"));

        var pkgDir = LocateUp("tests", "plugins", "PyVoice");
        var entry = "plugin/main.py";
        var host = SidecarRegistry.For("com.tunelab.test.pyvoice", pkgDir, python, entry);
        host.AutoRestart = true;
        host.ExpectEngine("voice", "PySine");
        var engine = new RemoteVoiceEngine(host, "PySine", hasSettings: false);
        var ctx = new VoiceTestContext(
            new VoiceTestNote("n0", 0.0, 0.5, 60, "la"),
            new VoiceTestNote("n1", 0.5, 1.0, 64, "du"));
        IVoiceSynthesisSession? session = null;
        try
        {
            engine.Init();
            Assert.True(host.AudioPlaneShm, "python facade must negotiate the shm audio plane (M4b)");
            Assert.Equal("sine", Assert.Single(engine.VoiceSourceInfos.Keys));

            session = engine.CreateSession(ctx);
            Assert.Equal("la", session.DefaultLyric);
            Assert.False(session.IsContinuation(ctx.Notes.First()!));

            var first = session.GetNextPendingSynthesisRange(0, 1.0) ?? throw new Exception("expected first chunk");
            Assert.Equal(0.0, first.StartTime, 6);
            Assert.Equal(0.5, first.EndTime, 6);

            session.SynthesizeNext(0, 0.5).GetAwaiter().GetResult();

            var seg = Assert.Single(ctx.Segments);
            Assert.True(seg.Committed);
            Assert.Equal(22050, seg.Written.Count);
            Assert.True(Math.Abs(seg.Written.Max(Math.Abs) - 0.2f) < 0.01f);        // 振幅 0.2 的正弦
            var syll = session.SynthesizedPhonemes["n0"];
            Assert.Equal("la", Assert.Single(syll.BodyPhonemes).Symbol);
            var st = Assert.Single(session.Status);
            Assert.Equal(SynthesisSegmentStatus.Synthesized, st.Status);
            Assert.Equal(62.0, session.SynthesizedPitch.Segments[0][1].Y, 6);

            // 第二拍：无事件则 peek 命中缓存（§3.5 设计语义）；触发 dirty 后重问 py 游标推进
            session.SynthesizeNext(0.5, 1.0).GetAwaiter().GetResult();
            Assert.Equal(2, ctx.Segments.Count);
            ctx.TriggerPitchRange(0, 1);                       // dirty++ → peek 重问
            Assert.Null(session.GetNextPendingSynthesisRange(0, 1.0));   // py 游标已到 1.0
        }
        finally
        {
            session?.Dispose();
            host.Shutdown();
        }
    }

    [Fact(Timeout = 60_000)]
    public void RealPythonEffectGainRoundTrip()
    {
        if (!RuntimeLocator.Default.TryResolve(ParseSpec("python>=3.10"), out var python, out var trail))
        {
            if (Environment.GetEnvironmentVariable("TUNELAB_REQUIRE_PYTHON") == "1")
                Assert.Fail("python not resolved: " + trail);
            return;   // 无解释器环境：vacuous pass
        }
        Environment.SetEnvironmentVariable("PYTHONPATH", LocateUp("scripting", "python"));

        var pkgDir = LocateUp("tests", "plugins", "PyEffect");
        var entry = "plugin/main.py";
        var host = SidecarRegistry.For("com.tunelab.test.pyeffect", pkgDir, python, entry);
        host.ExpectEngine("effect", "PyGain");
        var engine = new RemoteEffectEngine(host, "PyGain", hasSettings: false);
        var ctx = new EffectTestContext();
        try
        {
            engine.Init();
            Assert.True(host.AudioPlaneShm, "python facade must negotiate the shm audio plane (M4b)");
            // 声明面经真 config wire 重建
            var cfg = Assert.IsType<TuneLab.SDK.ObjectConfig>(engine.GetPropertyConfig(new EffectTestPropContext()));
            Assert.IsType<TuneLab.SDK.SliderConfig>(cfg.Properties["amount".AsKey()]);

            var session = engine.CreateSession(ctx);
            session.Process(System.Threading.CancellationToken.None).GetAwaiter().GetResult();

            var seg = Assert.Single(ctx.Segments);
            Assert.True(seg.Committed);
            Assert.Equal(4410, seg.Written.Count);
            for (int i = 0; i < seg.Written.Count; i += 701)     // 跨语言：python 写回 = 输入 × 0.5
                Assert.Equal(ctx.Input.Buffer[i] * 0.5f, seg.Written[i], 6);
            var loud = session.SynthesizedParameters["loudness"];
            Assert.Single(loud.Segments);
            Assert.Equal(2, loud.Segments[0].Count);
            var status = Assert.Single(session.Status);
            Assert.Equal(SynthesisSegmentStatus.Synthesized, status.Status);
        }
        finally
        {
            host.Shutdown();
        }
    }
}
