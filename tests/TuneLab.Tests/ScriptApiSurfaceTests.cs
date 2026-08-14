using System;
using System.Collections.Generic;
using System.Threading;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.Scripting;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 脚本 API 的【运行期】封条：info 层（getInfo() / addX(info)）、游离句柄与跨轨迁移、part 三元组几何、
// 删掉的糖。这些东西编译器管不着——info 层整条链是 JsValue 进、JsValue 出（Jint 的 camelCase↔PascalCase
// 映射、可选参数、JsObject/JsArray 构造），只有真跑一遍脚本才能验。
//
// 不经 UI：直接建 Project + ScriptRunner.Run。故这里刻意只覆盖【不需要音源引擎 / 不需要合成产物】的面
// （音素与自动化轨依赖音源声明，留给 tests/SCRIPT-API-SYMMETRY-TEST-CASES.md 的人工用例）。
public class ScriptApiSurfaceTests
{
    // 建 MidiPart 就会构造 SoundSource，而它要向 VoicesManager 求声明 config —— 那条路以「空引擎」兜底
    // （`GetInitedEngine(string.Empty)!`），空引擎又只在内建加载时注册。无 UI 的测试进程里没人加载过，
    // 故这里补上；幂等（RegisterEngine 同 id 重注册无副作用），静态构造里跑一次即可。
    static ScriptApiSurfaceTests()
    {
        TuneLab.Extensions.Voices.VoicesManager.LoadBuiltIn();
    }

    // 跑一段脚本，返回 (输出, 结果文本)；断言它没出错。
    static (string Output, string? Result) Run(IProject project, string code)
    {
        var result = ScriptRunner.Run(project, null, null, null, null, null,
            ScriptLimits.Interactive, code, CancellationToken.None);
        Assert.True(result.Ok, "script failed: " + result.Error + "\noutput:\n" + result.Output);
        return (result.Output, result.ResultText);
    }

    // 跑一段【预期抛错】的脚本，返回错误消息。
    static string RunExpectingError(IProject project, string code)
    {
        var result = ScriptRunner.Run(project, null, null, null, null, null,
            ScriptLimits.Interactive, code, CancellationToken.None);
        Assert.False(result.Ok, "script was expected to throw but succeeded; output:\n" + result.Output);
        Assert.False(result.Committed);
        return result.Error!;
    }

    // 两条轨：第一条带一个 4 拍的 midi part（3 个音符 + 一段音高线），第二条空。
    // 必须挂在 ProjectDocument 下：撤销栈（Head / Commit / DiscardTo）住在 document 上，裸 Project 的
    // Head 会一路上溯到空 parent 而 NRE——这也正是脚本运行"整段一次 Commit / 出错原子回退"的落点。
    static ProjectDocument SampleDocument()
    {
        var document = new ProjectDocument();
        document.SetProject(SampleProjectData());
        return document;
    }

    static Project SampleProjectData()
    {
        var part = new MidiPartInfo { Name = "p", Pos = 480, StartOffset = 0, EndOffset = 1920, Gain = -2 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "do" });
        part.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 62, Lyric = "re", Pronunciation = "r e" });
        part.Notes.Add(new NoteInfo { Pos = 960, Dur = 480, Pitch = 64, Lyric = "mi" });
        part.Pitch.Segments.Add(new List<Point> { new(0, 60), new(480, 61.5) });
        part.Properties = new PropertyObject(new Map<string, PropertyValue> { { "tension", 0.75 } });

        // 逐轨导出开关在宿主内部子类上（SDK 的 TrackInfo 不带——对别家工程格式是无效信息）。
        var track = new NativeTrackInfo { Name = "t1", Gain = -3, Pan = 0.25, AsRefer = false, Color = "#123456", ExportChannels = 2 };
        track.Parts.Add(part);

        return new Project(new ProjectInfo { Tracks = { track, new TrackInfo { Name = "t2" } } });
    }

    // ── info 往返 ──

    [Fact]
    public void NoteInfoRoundTripsThroughAddNote()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const p = tl.currentProject().tracks()[0].parts()[0];
            const n = p.notes()[1];
            const before = n.getInfo();
            const copy = p.addNote(before);
            const after = copy.getInfo();
            print(JSON.stringify(before) === JSON.stringify(after));
            print(before.pos + "|" + before.pronunciation + "|" + before.lyric);
            """);
        Assert.Contains("true", output);
        // pos 是【绝对】tick：part 锚点 480 + 音符相对 480 = 960。
        Assert.Contains("960|r e|re", output);
    }

    [Fact]
    public void PartInfoIsPureDataAndDoesNotTouchTheProject()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const p = tl.currentProject().tracks()[0].parts()[0];
            const info = p.getInfo();
            info.pos += 4800;
            info.name = "changed";
            print(p.pos + "|" + p.name + "|" + info.pos + "|" + info.name);
            """);
        Assert.Contains("480|p|5280|changed", output);
        Assert.Equal("p", ((IProject)project).Tracks[0].Parts.First!.Name.Value);
    }

    [Fact]
    public void PartInfoCarriesEveryDimension()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const info = tl.currentProject().tracks()[0].parts()[0].getInfo();
            print([info.type, info.name, info.pos, info.startOffset, info.endOffset, info.gain].join("|"));
            print("notes=" + info.notes.length + " pitchSegments=" + info.pitch.segments.length +
                  " tension=" + info.properties.tension);
            // note 内的 tick 也是绝对的（part 锚点 480 加回去了）
            print("notePos=" + info.notes.map(n => n.pos).join(","));
            print("pitchTicks=" + info.pitch.segments[0].map(pt => pt.tick).join(","));
            """);
        Assert.Contains("midi|p|480|0|1920|-2", output);
        Assert.Contains("notes=3 pitchSegments=1 tension=0.75", output);
        Assert.Contains("notePos=480,960,1440", output);
        Assert.Contains("pitchTicks=480,960", output);
    }

    [Fact]
    public void AddPartFromInfoReproducesEveryDimensionInPlace()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const t = tl.currentProject().tracks()[0];
            const src = t.parts()[0];
            const copy = t.addPart(src.getInfo());     // 原封不动 = 原位精确副本
            print("parts=" + t.parts().length);
            print("copy: pos=" + copy.pos + " notes=" + copy.notes().length + " gain=" + copy.gain +
                  " tension=" + copy.getProperty("tension"));
            print("copyNotePos=" + copy.notes().map(n => n.pos).join(","));
            """);
        Assert.Contains("parts=2", output);
        Assert.Contains("copy: pos=480 notes=3 gain=-2 tension=0.75", output);
        Assert.Contains("copyNotePos=480,960,1440", output);
    }

    // 平移是【句柄】的事，不是改 info 的事——因为 info 里的位置一律绝对，只改 info.pos 会把窗口挪走而
    // 内容留在原绝对位置（那是"内容在 part 里滑动"，通常不是调用方想要的）。这条封的就是安全的那条路。
    [Fact]
    public void ShiftingACopyIsDoneOnTheHandleNotOnTheInfo()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const t = tl.currentProject().tracks()[0];
            const src = t.parts()[0];
            const copy = t.addPart(src.getInfo());
            copy.pos += copy.dur;                     // 紧接源之后，内容跟随
            print("copy: pos=" + copy.pos + " startPos=" + copy.startPos + " endPos=" + copy.endPos);
            print("copyNotePos=" + copy.notes().map(n => n.pos).join(","));
            print("srcNotePos=" + src.notes().map(n => n.pos).join(","));
            """);
        Assert.Contains("copy: pos=2400 startPos=2400 endPos=4320", output);
        // 内容随锚点整体平移 1920，且源不受影响
        Assert.Contains("copyNotePos=2400,2880,3360", output);
        Assert.Contains("srcNotePos=480,960,1440", output);
    }

    // 反面封条：info 里的 tick 是【绝对】的，故只改 info.pos 只挪窗口、内容不跟随。这不是 bug 而是
    // "全绝对"口径的必然推论，钉在这里以免将来有人以为它会平移内容。
    [Fact]
    public void EditingInfoPosReframesTheWindowWithoutMovingContent()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const t = tl.currentProject().tracks()[0];
            const info = t.parts()[0].getInfo();
            info.pos += 1920;
            const copy = t.addPart(info);
            print("copy: pos=" + copy.pos + " notes=" + copy.notes().map(n => n.pos).join(","));
            """);
        Assert.Contains("copy: pos=2400 notes=480,960,1440", output);
    }

    [Fact]
    public void AddTrackFromInfoDuplicatesTheWholeTrack()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const project = tl.currentProject();
            const info = project.tracks()[0].getInfo();
            info.name = "copy";
            const dst = project.addTrack(info);
            print([dst.name, dst.gain, dst.pan, dst.asRefer, dst.color].join("|"));
            print("parts=" + dst.parts().length + " notes=" + dst.parts()[0].notes().length);
            print("exportOnSurface=" + ("exportEnabled" in info) + "," + ("exportChannels" in info));
            print("trackCount=" + project.tracks().length);
            """);
        Assert.Contains("copy|-3|0.25|false|#123456", output);
        Assert.Contains("parts=1 notes=3", output);
        // 导出配置整个不在脚本面（既非句柄字段也不在 info 里）：它是"一次导出动作的参数"、不是可撤销的工程
        // 数据，写它不入撤销栈。agent 要改导出设置走工具面（export_project）。故副本的导出开关落默认值。
        Assert.Contains("exportOnSurface=false,false", output);
        Assert.Contains("trackCount=3", output);
        Assert.False(((IProject)project).Tracks[2].ExportEnabled);
        Assert.Equal(1, ((IProject)project).Tracks[2].ExportChannels);
    }

    [Fact]
    public void AddTrackHonoursTheIndexArgument()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const project = tl.currentProject();
            project.addTrack({ name: "first" }, 0);
            print(project.tracks().map(t => t.name).join(","));
            """);
        Assert.Contains("first,t1,t2", output);
    }

    // ── 游离句柄与跨轨迁移 ──

    [Fact]
    public void RemovePartReturnsADetachedHandleThatCanBeInsertedOnAnotherTrack()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const [a, b] = tl.currentProject().tracks();
            const p = a.parts()[0];
            const returned = a.removePart(p);
            print("sameHandle=" + (returned === p) + " a=" + a.parts().length + " b=" + b.parts().length);
            print("readableWhileDetached: notes=" + p.notes().length + " pos=" + p.pos);
            b.insertPart(p);
            print("after: a=" + a.parts().length + " b=" + b.parts().length + " track=" + p.track().name);
            print("contentIntact: notes=" + p.notes().length + " gain=" + p.gain);
            """);
        Assert.Contains("sameHandle=true a=0 b=0", output);
        Assert.Contains("readableWhileDetached: notes=3 pos=480", output);
        Assert.Contains("after: a=0 b=1 track=t2", output);
        Assert.Contains("contentIntact: notes=3 gain=-2", output);
    }

    [Fact]
    public void WritingToADetachedHandleThrowsAndPointsTheWayBack()
    {
        var project = SampleDocument().Project!;
        var error = RunExpectingError(project, """
            const a = tl.currentProject().tracks()[0];
            const p = a.parts()[0];
            a.removePart(p);
            p.name = "nope";
            """);
        Assert.Contains("detached", error);
        Assert.Contains("track.insertPart(part)", error);
    }

    [Fact]
    public void ANoteCannotBeMovedToAnotherPart()
    {
        var project = SampleDocument().Project!;
        var error = RunExpectingError(project, """
            const t = tl.currentProject().tracks()[0];
            const p1 = t.parts()[0];
            const p2 = t.addPart({ pos: 4800, endOffset: 480 });
            const n = p1.notes()[0];
            p1.removeNote(n);
            p2.insertNote(n);
            """);
        Assert.Contains("addNote(note.getInfo())", error);
    }

    [Fact]
    public void ADetachedNoteCanBeInsertedBackOnItsOwnPart()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const p = tl.currentProject().tracks()[0].parts()[0];
            const n = p.notes()[0];
            p.removeNote(n);
            print("after remove: " + p.notes().length);
            p.insertNote(n);
            print("after insert: " + p.notes().length + " pitch=" + n.pitch);
            """);
        Assert.Contains("after remove: 2", output);
        Assert.Contains("after insert: 3 pitch=60", output);
    }

    // ── part 三元组几何 ──

    [Fact]
    public void AssigningPosMovesThePartAndItsContent()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const p = tl.currentProject().tracks()[0].parts()[0];
            print("before: " + [p.pos, p.startOffset, p.endOffset, p.startPos, p.endPos, p.dur].join("|"));
            p.pos += 1920;
            print("after:  " + [p.pos, p.startOffset, p.endOffset, p.startPos, p.endPos, p.dur].join("|"));
            print("notes=" + p.notes().map(n => n.pos).join(","));
            """);
        Assert.Contains("before: 480|0|1920|480|2400|1920", output);
        Assert.Contains("after:  2400|0|1920|2400|4320|1920", output);
        // 内容以锚点为原点，故绝对位置整体 +1920
        Assert.Contains("notes=2400,2880,3360", output);
    }

    [Fact]
    public void EdgeOffsetsTrimWithoutMovingTheAnchorOrTheContent()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const p = tl.currentProject().tracks()[0].parts()[0];
            p.startOffset += 240;
            p.endOffset   -= 240;
            print([p.pos, p.startOffset, p.endOffset, p.startPos, p.endPos, p.dur].join("|"));
            print("notes=" + p.notes().map(n => n.pos).join(","));
            """);
        Assert.Contains("480|240|1680|720|2160|1440", output);
        // 裁剪只改偏移、不动内容
        Assert.Contains("notes=480,960,1440", output);
    }

    [Fact]
    public void DerivedGeometryIsReadOnly()
    {
        var project = SampleDocument().Project!;
        // Jint 对只读 CLR 属性的赋值不抛错（静默忽略），故这里验"值没变"而不是"抛错"。
        var (output, _) = Run(project, """
            const p = tl.currentProject().tracks()[0].parts()[0];
            const before = [p.startPos, p.endPos, p.dur].join("|");
            try { p.dur = 99; } catch (e) { print("threw"); }
            try { p.startPos = 99; } catch (e) { print("threw"); }
            print("unchanged=" + (before === [p.startPos, p.endPos, p.dur].join("|")));
            """);
        Assert.Contains("unchanged=true", output);
    }

    [Fact]
    public void AnEmptyPartMustHaveAPositiveLength()
    {
        var project = SampleDocument().Project!;
        var error = RunExpectingError(project, """
            tl.currentProject().tracks()[1].addPart({ pos: 0 });
            """);
        Assert.Contains("endOffset", error);
    }

    // ── 新补的字段与上行引用 ──

    [Fact]
    public void TrackHasAsReferAndColorAndPartHasGain()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const t = tl.currentProject().tracks()[0];
            const p = t.parts()[0];
            t.asRefer = true;
            t.color = "#FF8800";
            p.gain = -6;
            print([t.asRefer, t.color, p.gain].join("|"));
            """);
        Assert.Contains("true|#FF8800|-6", output);
        var track = ((IProject)project).Tracks[0];
        Assert.True(track.AsRefer.Value);
        Assert.Equal("#FF8800", track.Color.Value);
    }

    [Fact]
    public void UpwardReferencesResolveTheParent()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const t = tl.currentProject().tracks()[0];
            const p = t.parts()[0];
            const n = p.notes()[0];
            print("part.track=" + (p.track() === t) + " note.part=" + (n.part() === p));
            """);
        Assert.Contains("part.track=true note.part=true", output);
    }

    [Fact]
    public void ContinuousAndPiecewiseAutomationIdsAreListedSeparately()
    {
        var project = SampleDocument().Project!;
        // 无音源 → 两张表都空，但关键是【互不重叠】且各自方法只认自己那张表。
        var (output, _) = Run(project, """
            const p = tl.currentProject().tracks()[0].parts()[0];
            const a = p.automationIds(), b = p.piecewiseAutomationIds();
            print("continuous=" + JSON.stringify(a) + " piecewise=" + JSON.stringify(b));
            print("disjoint=" + !a.some(id => b.includes(id)));
            """);
        Assert.Contains("disjoint=true", output);
    }

    // ── 固定（lock）：没有产物时如实返回 false，用法错误则报错 ──
    // 「有产物时固定出正确的曲线」依赖真实引擎的回显，留给人工用例；这里封的是【无产物 / 用法错】这两条
    // 自动可验的边界——尤其是 no-op 必须可被脚本看见（agent 那边没人盯着屏幕，静默成功最坑）。

    [Fact]
    public void LockingWithoutSynthesisOutputReturnsFalseInsteadOfPretendingToSucceed()
    {
        var document = SampleDocument();
        var result = ScriptRunner.Run(document.Project!, null, null, null, null, null, ScriptLimits.Interactive, """
            const p = tl.currentProject().tracks()[0].parts()[0];
            print("locked=" + p.lockPitch());
            """, CancellationToken.None);
        Assert.True(result.Ok, result.Error);
        Assert.Contains("locked=false", result.Output);
        // 什么都没写 ⇒ 不该留下一个空的撤销步骤
        Assert.False(result.Committed);
    }

    [Theory]
    [InlineData("p.lockPitch(0);", "BOTH startTick and endTick")]
    [InlineData("p.lockPitch(undefined, 1920);", "BOTH startTick and endTick")]
    [InlineData("p.lockPitch(960, 960);", "endTick must be greater")]
    [InlineData("p.lockAutomation('nope');", "unknown automation")]
    public void LockArgumentsAreValidated(string code, string expected)
    {
        var error = RunExpectingError(SampleDocument().Project!,
            "const p = tl.currentProject().tracks()[0].parts()[0];\n" + code);
        Assert.Contains(expected, error);
    }

    // 音素侧与曲线侧同一个动词：`lock`。这里只验名字在（无合成产物时是 no-op），语义由音素那套用例覆盖。
    [Fact]
    public void PhonemesUseTheSameLockVerbAsCurves()
    {
        var (output, _) = Run(SampleDocument().Project!, """
            const n = tl.currentProject().tracks()[0].parts()[0].notes()[0];
            print("before=" + n.hasLockedPhonemes);
            n.lockPhonemes();                       // 无合成音素 ⇒ no-op
            print("after=" + n.hasLockedPhonemes + " phonemes=" + n.phonemes().length);
            """);
        Assert.Contains("before=false", output);
        Assert.Contains("after=false phonemes=0", output);
    }

    // 先问后做：没有配对回显的轨（这里连轨都没有，音源为空）不必靠"试着 lock 然后整段回退"来发现。
    [Fact]
    public void HasSynthesizedParameterAnswersWithoutThrowing()
    {
        var (output, _) = Run(SampleDocument().Project!, """
            const p = tl.currentProject().tracks()[0].parts()[0];
            print("hasSynthesizedParameter=" + p.hasSynthesizedParameter("Volume"));
            """);
        Assert.Contains("hasSynthesizedParameter=false", output);
    }

    // ── 速度 / 拍号标记的删除 ──

    [Fact]
    public void TempoMarkersCanBeAddedAndRemoved()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const project = tl.currentProject();
            project.setTempo(140, 1920);
            print("added=" + project.tempos().map(t => t.bpm + "@" + t.tick).join(","));
            project.removeTempo(1920);
            print("removed=" + project.tempos().map(t => t.bpm + "@" + t.tick).join(","));
            """);
        Assert.Contains("added=120@0,140@1920", output);
        Assert.Contains("removed=120@0", output);
    }

    [Fact]
    public void RemovingAMissingTempoMarkerThrowsInsteadOfDoingNothing()
    {
        var error = RunExpectingError(SampleDocument().Project!, "tl.currentProject().removeTempo(1920);");
        Assert.Contains("no tempo marker", error);
    }

    [Fact]
    public void TheBaseTempoMarkerCannotBeRemoved()
    {
        var error = RunExpectingError(SampleDocument().Project!, "tl.currentProject().removeTempo(0);");
        Assert.Contains("base tempo", error);
    }

    [Fact]
    public void TimeSignatureMarkersCanBeAddedAndRemoved()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const project = tl.currentProject();
            project.setTimeSignature(3, 4, 3);
            print("added=" + project.timeSignatures().map(s => s.numerator + "/" + s.denominator + "@" + s.bar).join(","));
            project.removeTimeSignature(3);
            print("removed=" + project.timeSignatures().map(s => s.numerator + "/" + s.denominator + "@" + s.bar).join(","));
            """);
        Assert.Contains("added=4/4@1,3/4@3", output);
        Assert.Contains("removed=4/4@1", output);
    }

    // 颤音一族（完整 info + 两张影响表）【不在此自动化】：InsertVibrato 会驱动合成管线重建，而
    // VoiceSynthesisPipeline 明确要求"建在数据线程上"（构造时 SynchronizationContext.Current 非空、
    // 之后活视图只许该线程访问）。那是 GUI / agent 宿主的环境，装个裸 SynchronizationContext 反而会把
    // 重建 Post 到线程池上炸掉。故颤音影响表留给 tests/SCRIPT-API-SYMMETRY-TEST-CASES.md 的人工用例 13。

    // ── 导出设置：在脚本面（用户会要「一键设成我的预设」），但是【设置项、不入撤销栈】 ──

    [Fact]
    public void ExportSettingsAreWritableFromScripts()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const p = tl.currentProject();
            print("before=" + [p.exportFormat, p.exportSampleRate, p.exportBitDepth, p.masterExportChannels].join("|"));
            p.exportPath = "D:/renders";
            p.exportFileName = "take1";
            p.exportFormat = "flac";
            p.exportSampleRate = 96000;
            p.exportBitDepth = 24;
            p.exportBitrate = 256;
            p.masterExportEnabled = false;
            p.masterExportChannels = 1;
            const t = p.tracks()[0];
            t.exportEnabled = true;
            t.exportChannels = 1;
            print("after=" + [p.exportPath, p.exportFileName, p.exportFormat, p.exportSampleRate,
                              p.exportBitDepth, p.exportBitrate, p.masterExportEnabled, p.masterExportChannels].join("|"));
            print("track=" + t.exportEnabled + "|" + t.exportChannels);
            """);
        Assert.Contains("before=wav|44100|16|2", output);
        Assert.Contains("after=D:/renders|take1|flac|96000|24|256|false|1", output);
        Assert.Contains("track=true|1", output);
        Assert.Equal("flac", ((IProject)project).ExportFormat);
        Assert.True(((IProject)project).Tracks[0].ExportEnabled);
    }

    [Theory]
    [InlineData("tl.currentProject().exportFormat = 'aiff';", "unknown export format")]
    [InlineData("tl.currentProject().exportSampleRate = 0;", "exportSampleRate must be positive")]
    [InlineData("tl.currentProject().masterExportChannels = 3;", "must be 1 (mono) or 2 (stereo)")]
    [InlineData("tl.currentProject().tracks()[0].exportChannels = 5;", "must be 1 (mono) or 2 (stereo)")]
    public void ExportSettingsAreValidated(string code, string expected)
    {
        Assert.Contains(expected, RunExpectingError(SampleDocument().Project!, code));
    }

    // 导出设置不产生撤销命令，故 DiscardTo 管不着它——ScriptContext 靠写前留底自己还原。这条封的就是
    // 「出错/preview 原子回退」在这些非撤销字段上同样成立。
    [Fact]
    public void AThrowingScriptRestoresExportSettingsToo()
    {
        var project = (IProject)SampleDocument().Project!;
        RunExpectingError(project, """
            const p = tl.currentProject();
            p.exportPath = "D:/renders";
            p.exportFormat = "mp3";
            p.tracks()[0].exportEnabled = true;
            throw new Error("boom");
            """);
        Assert.Equal(string.Empty, project.ExportPath);
        Assert.Equal("wav", project.ExportFormat);
        Assert.False(project.Tracks[0].ExportEnabled);
    }

    [Fact]
    public void PreviewRunDoesNotLeakExportSettings()
    {
        var project = (IProject)SampleDocument().Project!;
        var result = ScriptRunner.Run(project, null, null, null, null, null, ScriptLimits.Interactive, """
            tl.currentProject().exportFormat = "ogg";
            tl.currentProject().tracks()[0].exportChannels = 1;
            """, CancellationToken.None, preview: true);
        Assert.True(result.Ok, result.Error);
        Assert.False(result.Committed);
        Assert.Equal("wav", project.ExportFormat);
        Assert.Equal(2, project.Tracks[0].ExportChannels);   // 夹具原值 2，预览改成 1 后应还原
    }

    // 反面：成功提交后【不】还原，且导出设置刻意不进撤销栈（Ctrl+Z 不会把导出路径退回去）——与在导出
    // 侧栏里改它们的行为一致。
    [Fact]
    public void ExportSettingsSurviveAnUndoOfTheSameRun()
    {
        var document = SampleDocument();
        var project = (IProject)document.Project!;
        var result = ScriptRunner.Run(project, null, null, null, null, null, ScriptLimits.Interactive, """
            tl.currentProject().exportFormat = "flac";
            tl.currentProject().addTrack({ name: 'added' });
            """, CancellationToken.None);
        Assert.True(result.Ok, result.Error);
        Assert.True(result.Committed);

        Assert.True(document.Undo());
        Assert.Equal(2, project.Tracks.Count);          // 工程数据回退了
        Assert.Equal("flac", project.ExportFormat);     // 设置项没回退（刻意）
    }

    // ── 删掉的糖 ──

    [Theory]
    [InlineData("tl.currentProject().tracks()[0].set({ name: 'x' });")]
    [InlineData("tl.currentProject().tracks()[0].duplicate();")]
    [InlineData("tl.currentProject().tracks()[0].parts()[0].set({ name: 'x' });")]
    [InlineData("tl.currentProject().tracks()[0].parts()[0].duplicate();")]
    [InlineData("tl.currentProject().tracks()[0].parts()[0].notesInRange(0, 480);")]
    [InlineData("tl.currentProject().tracks()[0].parts()[0].notes()[0].set({ pitch: 60 });")]
    [InlineData("tl.currentProject().tracks()[0].parts()[0].notes()[0].addPhoneme({ symbol: 'a' });")]
    // 音素侧曾叫 pinPhonemes；脚本面统一到 lock 后旧名必须真的消失（留着就是同一件事两个名字）。
    [InlineData("tl.currentProject().tracks()[0].parts()[0].notes()[0].pinPhonemes();")]
    public void RemovedSugarIsGone(string code)
    {
        var error = RunExpectingError(SampleDocument().Project!, code);
        Assert.Contains("not a function", error);
    }

    // ── 回归：原子性 ──

    [Fact]
    public void AThrowingScriptLeavesTheProjectUntouched()
    {
        var project = SampleDocument().Project!;
        var before = ((IProject)project).Tracks[0].Parts.First!;
        int noteCount = ((IMidiPart)before).Notes.Count;

        var error = RunExpectingError(project, """
            const p = tl.currentProject().tracks()[0].parts()[0];
            for (const n of p.notes()) { const i = n.getInfo(); i.pitch += 4; p.addNote(i); }
            tl.currentProject().addTrack({ name: "doomed" });
            throw new Error("boom");
            """);
        Assert.Contains("boom", error);
        Assert.Equal(noteCount, ((IMidiPart)before).Notes.Count);
        Assert.Equal(2, ((IProject)project).Tracks.Count);
    }

    [Fact]
    public void ASuccessfulScriptCommitsAsOneUndoableChange()
    {
        var document = SampleDocument();
        var project = (IProject)document.Project!;
        var result = ScriptRunner.Run(project, null, null, null, null, null, ScriptLimits.Interactive,
            "tl.currentProject().addTrack({ name: 'added' });", CancellationToken.None);
        Assert.True(result.Ok, result.Error);
        Assert.True(result.Committed);
        Assert.Equal(3, project.Tracks.Count);

        Assert.True(document.Undo());
        Assert.Equal(2, project.Tracks.Count);
        Assert.True(document.Redo());
        Assert.Equal(3, project.Tracks.Count);
    }

    [Fact]
    public void CrossTrackMigrationUndoesAsOneStep()
    {
        var document = SampleDocument();
        var project = (IProject)document.Project!;
        var result = ScriptRunner.Run(project, null, null, null, null, null, ScriptLimits.Interactive, """
            const [a, b] = tl.currentProject().tracks();
            b.insertPart(a.removePart(a.parts()[0]));
            """, CancellationToken.None);
        Assert.True(result.Ok, result.Error);
        Assert.True(result.Committed);

        var tracks = project.Tracks;
        Assert.Empty(tracks[0].Parts);
        Assert.Single(tracks[1].Parts);

        // 摘出 + 插入是两条命令，但整段脚本折成【一次】Commit，故一步 Undo 就回到原样。
        Assert.True(document.Undo());
        Assert.Single(tracks[0].Parts);
        Assert.Empty(tracks[1].Parts);
        // 换父后 Track 反向指针也随撤销复位（由集合的 ItemAdded 重建，不必额外记录命令）
        Assert.Same(tracks[0], tracks[0].Parts.First!.Track);
    }

    // ── 集合方法返回什么：真 JS 数组、可变、但是【快照】 ──
    // 手册一直声称"返回普通数组"，这里把它连同可变性一起钉死：Jint 把 CLR 的 ScriptX[] 编排成真正的
    // JS Array（Array.isArray 为真、原型是 Array.prototype），故 map/filter/flatMap/slice/sort/spread/
    // 解构/JSON.stringify 全部可用。它可写，但写的是【你手里那份副本】——工程本体毫发无损。
    [Fact]
    public void CollectionMethodsReturnRealJsArrays()
    {
        var project = SampleDocument().Project!;
        var (output, _) = Run(project, """
            const t = tl.currentProject().tracks();
            print("isArray=" + Array.isArray(t) + " ctor=" + t.constructor.name + " length=" + t.length);
            print("methods=" + ["map","filter","flatMap","slice","sort","forEach","reduce"]
                .map(m => typeof t[m]).join(","));
            print("spread=" + [...t].length + " json=" + JSON.stringify(t.map(x => x.name)));
            """);
        Assert.Contains("isArray=true ctor=Array length=2", output);
        Assert.Contains("methods=function,function,function,function,function,function,function", output);
        Assert.Contains("spread=2 json=[\"t1\",\"t2\"]", output);
    }

    [Fact]
    public void EachCallReturnsAFreshSnapshotSoMutatingItCannotHarmTheProject()
    {
        var project = (IProject)SampleDocument().Project!;
        var (output, _) = Run(project, """
            const p = tl.currentProject();
            print("sameRef=" + (p.tracks() === p.tracks()));
            const t = p.tracks();
            t[0] = null;          // 往快照里塞垃圾
            t.push(null);
            t.sort();
            print("trashed: length=" + t.length);
            const fresh = p.tracks();
            print("fresh=" + fresh.length + " names=" + fresh.map(x => x.name).join(","));
            """);
        // 每次调用都是新数组（故不能指望 === ），乱改它对工程零影响
        Assert.Contains("sameRef=false", output);
        Assert.Contains("trashed: length=3", output);
        Assert.Contains("fresh=2 names=t1,t2", output);
        Assert.Equal(2, project.Tracks.Count);
        Assert.Equal("t1", project.Tracks[0].Name.Value);
    }

    // 推论：在快照上 sort() 只排你手里的数组，【不会】重排工程里的轨/音符顺序——想真调序得用
    // removeX + insertX(index)（轨）或改排序键（part/note/颤音按位置自排）。
    [Fact]
    public void SortingASnapshotDoesNotReorderTheProject()
    {
        var project = (IProject)SampleDocument().Project!;
        var (output, _) = Run(project, """
            const p = tl.currentProject();
            const desc = p.tracks().sort((a, b) => a.name < b.name ? 1 : -1);
            print("snapshotOrder=" + desc.map(x => x.name).join(","));
            print("projectOrder=" + p.tracks().map(x => x.name).join(","));
            """);
        Assert.Contains("snapshotOrder=t2,t1", output);
        Assert.Contains("projectOrder=t1,t2", output);
        Assert.Equal("t1", project.Tracks[0].Name.Value);
    }
}
