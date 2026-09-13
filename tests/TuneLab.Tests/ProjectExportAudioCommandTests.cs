using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using TuneLab.Data;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// `project export-audio` 的封条。
//
// 这条命令的价值几乎全在**它什么时候不写文件**，以及**写完之后怎么说话**：界面上的导出是拉 AudioGraph
// 此刻的数据、不等任何人（用户自己看着状态带等到全绿才按），照搬到命令面就会静默产出静音而回报仍说成功。
// 故这里钉三样：坏请求在动手前就被挡下（且不打扰用户）、闸门把「要跑完整合成、界面会锁住」说出来、
// 三种回报各说各的话——尤其「超时」与「会是静音」这两种，绝不能读成「导好了」。
//
// 【不跑合成、不写盘】渲染要真引擎与真编码器，那是实机的事；这里只测判据与措辞，故所有用例要么在
// 落地之前就返回，要么直接对着 Render 喂数据。
[Collection("DataThreadProject")]   // 见 FinalPronunciationTests 的 CollectionDefinition
public class ProjectExportAudioCommandTests
{
    static ProjectExportAudioCommandTests()
    {
        TestVoices.EnsureBuiltIn();
    }

    static readonly ProjectExportAudioCommand Command = new();

    static IProject SampleProject()
    {
        var part = new MidiPartInfo { Name = "lead", Pos = 0, StartOffset = 0, EndOffset = 1920 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "do" });

        var track = new NativeTrackInfo { Name = "vocal" };
        track.Parts.Add(part);

        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo
        {
            Tempos = { new TempoInfo { Pos = 0, Bpm = 120 } },
            TimeSignatures = { new TimeSignatureInfo { BarIndex = 0, Numerator = 4, Denominator = 4 } },
            Tracks = { track },
        }));
        return document.Project!;
    }

    sealed class RecordingPolicy : IAuthorizationPolicy
    {
        public AuthorizationRequest? Asked;
        public AuthorizationMode Mode => AuthorizationMode.Confirm;
        public bool CanAsk => true;
        public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        {
            Asked = request;
            // 恒拒：本套用例一次也不该真去渲染。被拒本身是命令的一条正常出路，不是错误。
            return Task.FromResult(AuthorizationDecision.Reject);
        }
    }

    static CommandResult Run(string json, IProject? project, IAuthorizationPolicy? policy = null)
        => Command.ExecuteAsync(CommandArgs.Parse(json),
            new CommandContext { Project = project, Authorization = policy ?? new RecordingPolicy() }, CancellationToken.None)
            .GetAwaiter().GetResult();

    static string Json(string path) => $$"""{"path": "{{path.Replace(@"\", @"\\")}}"}""";

    // ── 坏请求在动手之前就被挡下，且一次都不打扰用户

    [Fact]
    public void WithoutAProjectThereIsNothingToRender()
    {
        var result = Run(Json(@"C:\x\y.wav"), null);
        Assert.Equal("no_project", result.Error!.Value.Code);
    }

    // 扩展名就是格式：给了工程后缀名要指向 export_project，不能含糊成"格式不支持"。
    [Fact]
    public void TheExtensionPicksTheFormatAndAProjectExtensionIsSentElsewhere()
    {
        var policy = new RecordingPolicy();
        var result = Run(Json(Path.Combine(Path.GetTempPath(), "song.tlpx")), SampleProject(), policy);

        Assert.Equal("unsupported_format", result.Error!.Value.Code);
        Assert.Contains(".wav", result.Error!.Value.Message);
        Assert.Contains("export_project", result.Error!.Value.Message);
        Assert.Null(policy.Asked);
    }

    // 空工程要在闸门之前拦下：混音长度自带一秒尾，不拦就会一本正经地渲出一秒静音并回报成功。
    [Fact]
    public void AnEmptyProjectIsRefusedInsteadOfRenderingASecondOfSilence()
    {
        var policy = new RecordingPolicy();
        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo { Tracks = { new TrackInfo { Name = "empty" } } }));

        var result = Run(Json(Path.Combine(Path.GetTempPath(), "nothing.wav")), document.Project, policy);

        Assert.Equal("nothing_to_render", result.Error!.Value.Code);
        Assert.Null(policy.Asked);
    }
    [Fact]
    public void AMissingFolderIsReportedWithoutBotheringAnyone()
    {
        var policy = new RecordingPolicy();
        var result = Run(Json(@"C:\no\such\folder\mix.wav"), SampleProject(), policy);

        Assert.Equal("missing_folder", result.Error!.Value.Code);
        Assert.Null(policy.Asked);
    }

    // ── 闸门：用户凭这张卡片决定要不要把这台机器交出去好几分钟

    [Fact]
    public void TheGateSaysItWillRunTheWholeSynthesisAndLockTheWindow()
    {
        var policy = new RecordingPolicy();
        var result = Run(Json(Path.Combine(Path.GetTempPath(), "tunelab-export-audio-probe.wav")), SampleProject(), policy);

        Assert.Equal(WriteKind.ProjectExportAudio, policy.Asked!.Value.Kind);
        var phrase = policy.Asked!.Value.ActionPhrase();
        Assert.Contains("FULL synthesis", phrase);
        Assert.Contains("locked up", phrase);
        Assert.Contains("WAV", phrase);
        // 被拒是一条如实的出路，不是错误，且什么都没写。
        Assert.False(result.IsError);
        Assert.Equal("refused", result.Data!["outcome"]!.GetValue<string>());
    }

    [Fact]
    public void RenderingOntoAnExistingFileSaysReplace()
    {
        var existing = Path.Combine(Path.GetTempPath(), "tunelab-export-audio-existing.wav");
        File.WriteAllText(existing, "not really audio");
        try
        {
            var policy = new RecordingPolicy();
            Run(Json(existing), SampleProject(), policy);

            Assert.Equal(WriteKind.ProjectExportAudioOverwrite, policy.Asked!.Value.Kind);
            Assert.Contains("REPLACING the file already there", policy.Asked!.Value.ActionPhrase());
            Assert.Equal("not really audio", File.ReadAllText(existing));
        }
        finally
        {
            File.Delete(existing);
        }
    }

    // ── 回报：三种结局各说各的话

    // 超时的要害是「什么都没写」。半截音频比没有音频更坏——它看起来是成品。
    [Fact]
    public void ATimeoutSaysNothingWasWrittenAndHowMuchWasLeft()
    {
        var text = Command.Render(new JsonObject
        {
            ["path"] = @"C:\songs\mix.wav",
            ["outcome"] = "timeout",
            ["seconds"] = 300,
            ["busy"] = 2,
            ["pending"] = 5,
        }, CommandArgs.Empty);

        Assert.Contains("NOTHING was written", text);
        Assert.Contains("300s", text);
        Assert.Contains("2 part(s) still rendering, 5 still waiting", text);
        Assert.Contains("timeoutSeconds", text);   // 指出下一步怎么办
    }

    // 失败段会被导成静音。默认拒绝，并且必须点名坏在哪——否则调用方无从告诉用户要修什么。
    [Fact]
    public void FailedRangesRefuseTheExportAndAreNamed()
    {
        var text = Command.Render(new JsonObject
        {
            ["path"] = @"C:\songs\mix.wav",
            ["outcome"] = "would_be_silent",
            ["silent"] = new JsonArray
            {
                new JsonObject
                {
                    ["where"] = "track 1 part 2 (\"lead\")",
                    ["startSeconds"] = 4.5,
                    ["endSeconds"] = 9.25,
                    ["message"] = "engine said no",
                },
            },
        }, CommandArgs.Empty);

        Assert.Contains("Did NOT export", text);
        Assert.Contains("SILENT", text);
        Assert.Contains("track 1 part 2 (\"lead\")", text);
        Assert.Contains("4.5s–9.25s", text);
        Assert.Contains("engine said no", text);
        Assert.Contains("allowIncomplete", text);
    }

    // 成功那一支要说清三件事：文件是什么规格、等了多久、以及【这不是保存】。
    [Fact]
    public void SuccessReportsTheSpecAndSaysThisWasNotASave()
    {
        var text = Command.Render(new JsonObject
        {
            ["path"] = @"C:\songs\mix.wav",
            ["outcome"] = "applied",
            ["format"] = "wav",
            ["formatName"] = "WAV",
            ["sampleRate"] = 48000,
            ["channels"] = 2,
            ["bitDepth"] = 24,
            ["seconds"] = 12.5,
            ["bytes"] = 3600000L,   // 真实 Data 里 bytes 是 long（FileInfo.Length）
            ["overwrite"] = false,
            ["waitedSeconds"] = 41.2,
        }, CommandArgs.Empty);

        Assert.Contains("WAV, stereo, 48000 Hz, 24-bit", text);
        Assert.Contains("12.5s of audio", text);
        Assert.Contains("Synthesis took 41.2s", text);
        Assert.Contains("did not save the user's project", text);
    }

    // 有损格式报的是码率而不是位深（位深对它没有意义，报出来就是噪音）。
    [Fact]
    public void ALossyFormatReportsItsBitrateInstead()
    {
        var text = Command.Render(new JsonObject
        {
            ["path"] = @"C:\songs\mix.mp3",
            ["outcome"] = "applied",
            ["format"] = "mp3",
            ["formatName"] = "MP3",
            ["sampleRate"] = 44100,
            ["channels"] = 1,
            ["bitrate"] = 320,
            ["seconds"] = 3.0,
            ["bytes"] = 120000L,
            ["overwrite"] = true,
            ["waitedSeconds"] = 0.4,
        }, CommandArgs.Empty);

        Assert.Contains("MP3, mono, 44100 Hz, 320 kbps", text);
        Assert.Contains("replacing the file that was there", text);
    }

    // 降级只是警告：那些范围有声音，只是不是应有的那个。与静音分开说，否则用户分不清要不要重做。
    [Fact]
    public void DegradedRangesAreAWarningNotARefusal()
    {
        var text = Command.Render(new JsonObject
        {
            ["path"] = @"C:\songs\mix.wav",
            ["outcome"] = "applied",
            ["format"] = "wav",
            ["formatName"] = "WAV",
            ["sampleRate"] = 44100,
            ["channels"] = 2,
            ["bitDepth"] = 16,
            ["seconds"] = 8.0,
            ["bytes"] = 1400000L,
            ["overwrite"] = false,
            ["waitedSeconds"] = 5.0,
            ["degraded"] = new JsonArray
            {
                new JsonObject
                {
                    ["where"] = "track 2 part 1",
                    ["startSeconds"] = 0.0,
                    ["endSeconds"] = 8.0,
                    ["message"] = "Effect Reverb failed, playing unprocessed audio",
                },
            },
        }, CommandArgs.Empty);

        Assert.Contains("unprocessed audio there rather than the intended sound", text);
        Assert.Contains("track 2 part 1", text);
        Assert.DoesNotContain("Did NOT export", text);
    }
}
