using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.SDK;

namespace TuneLab.Extensions.Derivers;

// deriver 阶段二（廉价、宿主侧）：把 DerivedResult 换算成 tick 并作一条栈顶 undo 命令并入工程。
// 秒→tick 全在此侧算（part 锚点、note 相对 tick、Pitch 相对 X），插件零参与（见 IAudioDerivationEngine 单位纪律）。
// 不缓存：每次应用按当前工程时间线 + 当前裁剪重导（apply-inputs 可重选、不重跑模型）。仅数据线程调用。
internal static class DerivedResultApplier
{
    // apply-inputs（非缓存键、可重选）。v1 只暴露治理时间线开关；落地槽过滤 v1 全产。
    internal sealed class Options
    {
        // 勾选「同时套用检测速度」→ 先装检测速度图、再拿它换算音符秒（时间线自洽）。
        public bool ApplyDetectedTempo { get; init; }
        public bool ApplyDetectedTimeSignature { get; init; }
    }

    // 把结果并入工程。产物时间 = 源音频内容秒（0 = 源文件起点）。apply 侧几何：
    //   · anchorSeconds = 源 part 锚点 Pos 的工程秒（内容秒 t → 工程秒 = anchorSeconds + t，落到源 part 之下）；
    //   · [cropStart, cropEnd] = 源 part 裁剪窗口（内容秒）——落地过滤；与 DerivedPart 自身的 [Pos+StartOffset, Pos+EndOffset]
    //     求交得每个 part 的有效窗（重叠即落 + 边界裁断，完全窗外丢弃）。
    // sourceTrack = 源 part 所在轨（插其下）；null/已删 → 追加末尾。返回落地新轨数。整个过程折成一条 undo 步。
    public static int Apply(IProject project, ITrack? sourceTrack, double anchorSeconds, double cropStart, double cropEnd, DerivedResult result, Options options)
    {
        if (options.ApplyDetectedTempo && result.Tempos.Count > 0)
            project.TempoManager.SetInfo(BuildTempoInfos(result.Tempos));
        if (options.ApplyDetectedTimeSignature && result.TimeSignatures.Count > 0)
            project.TimeSignatureManager.SetInfo(BuildTimeSignatureInfos(project, result.TimeSignatures));

        int newTrackCount = 0;
        if (result.Tracks.Count > 0)
        {
            int targetIndex = IndexOfTrack(project, sourceTrack);
            targetIndex = targetIndex >= 0 ? targetIndex + 1 : project.Tracks.Count;

            foreach (var track in result.Tracks)
            {
                var info = BuildTrackInfo(project, track, anchorSeconds, cropStart, cropEnd, project.Tracks.Count + newTrackCount);
                project.AddTrack(info);
                var newTrack = project.Tracks[project.Tracks.Count - 1];
                if (targetIndex < project.Tracks.Count - 1)
                {
                    project.RemoveTrack(newTrack);          // 同实例 remove→insert 重排到源轨之下（同 TrackHead 拖拽范式）
                    project.InsertTrack(targetIndex, newTrack);
                }
                targetIndex++;
                newTrackCount++;
            }
        }

        project.Commit();
        return newTrackCount;
    }

    static int IndexOfTrack(IProject project, ITrack? track)
    {
        if (track == null)
            return -1;
        for (int i = 0; i < project.Tracks.Count; i++)
            if (ReferenceEquals(project.Tracks[i], track))
                return i;
        return -1;
    }

    static TrackInfo BuildTrackInfo(IProject project, DerivedTrack track, double anchorSeconds, double cropStart, double cropEnd, int colorIndex)
    {
        var tempo = project.TempoManager;
        return new TrackInfo
        {
            Name = track.Name ?? "Derived".Tr(TC.Document),
            Color = Style.GetNewColor(colorIndex),
            Parts = track.Parts.Select(p => BuildPartInfo(tempo, anchorSeconds, cropStart, cropEnd, p)).ToList(),
        };
    }

    static PartInfo BuildPartInfo(ITempoManager tempo, double anchorSeconds, double cropStart, double cropEnd, DerivedPart part)
    {
        double Tick(double contentSec) => tempo.GetTick(anchorSeconds + contentSec);

        // 有效窗 = 源输入裁剪窗 ∩ 该 part 自身 [StartTime, EndTime]（默认 0..+∞）；内容秒域。
        double winStart = Math.Max(cropStart, part.StartTime);
        double winEnd = Math.Min(cropEnd, part.EndTime);
        double posTick = Tick(winStart);   // part 锚点对齐有效窗起点（对齐露出内容）
        switch (part)
        {
            case DerivedMidiPart midi:
            {
                var info = new MidiPartInfo { Pos = posTick, StartOffset = 0, EndOffset = 0 };
                // 重叠即落 + 边界裁断，完全窗外丢弃。音素与 BodyOffset 秒基、逐字克隆落地、零转换（不随裁剪重排，v1 从简）。
                info.Notes = midi.Notes
                    .Select(n => (Start: Math.Max(n.StartTime, winStart), End: Math.Min(n.EndTime, winEnd), Note: n))
                    .Where(x => x.End > x.Start)
                    .Select(x => new NoteInfo
                    {
                        Pos = Tick(x.Start) - posTick,
                        Dur = Tick(x.End) - Tick(x.Start),
                        Pitch = x.Note.Pitch,
                        Lyric = x.Note.Lyric,
                        LeadingPhonemes = ClonePhonemes(x.Note.LeadingPhonemes),
                        BodyPhonemes = ClonePhonemes(x.Note.BodyPhonemes),
                        BodyOffset = x.Note.BodyOffset,
                    }).ToList();
                info.Pitch = midi.Pitch.Segments
                    .Select(seg => seg.Where(pt => pt.X >= winStart && pt.X <= winEnd).Select(pt => new Point(Tick(pt.X) - posTick, pt.Y)).ToList())
                    .Where(seg => seg.Count >= 2)
                    .ToList();
                info.EndOffset = ComputeMidiEndOffset(info);
                return info;
            }
            case DerivedAudioPart:
                // 音频产物 v1 不产（占位）；DerivedAudioPart 补负载字段后再实现落地。
                return new AudioPartInfo { Pos = posTick, Path = string.Empty };
            default:
                throw new InvalidOperationException("Unknown DerivedPart subtype: " + part.GetType().Name);
        }
    }

    // DerivedPhoneme → tick 域的 PhonemeInfo（成员同、秒基、零转换；创作字段 Properties 默认填 null）。
    static List<PhonemeInfo> ClonePhonemes(IReadOnlyList<DerivedPhoneme> phonemes)
        => phonemes.Select(p => new PhonemeInfo { Symbol = p.Symbol, Duration = p.Duration, StretchWeight = p.StretchWeight }).ToList();

    static double ComputeMidiEndOffset(MidiPartInfo info)
    {
        double end = 0;
        foreach (var note in info.Notes)
            end = Math.Max(end, note.Pos + note.Dur);
        foreach (var seg in info.Pitch)
            foreach (var pt in seg)
                end = Math.Max(end, pt.X);
        return end;
    }

    // 检测速度图（秒锚点→BPM）→ tick 锚点 TempoInfo：首点锚 tick 0，其后按前段 BPM 累积积分。v1 基本形。
    static List<TempoInfo> BuildTempoInfos(IReadOnlyList<DerivedTempo> tempos)
    {
        var sorted = tempos.OrderBy(t => t.Time).ToList();
        var infos = new List<TempoInfo> { new() { Pos = 0, Bpm = sorted[0].Bpm } };
        double tick = 0;
        for (int i = 1; i < sorted.Count; i++)
        {
            tick += (sorted[i].Time - sorted[i - 1].Time) * sorted[i - 1].Bpm / 60.0 * MusicTheory.RESOLUTION;
            infos.Add(new TempoInfo { Pos = tick, Bpm = sorted[i].Bpm });
        }
        return infos;
    }

    // 检测拍号（秒锚点 + n/d）→ 小节序号锚点：按已装好的时间线把秒→tick→最近小节序。
    static List<TimeSignatureInfo> BuildTimeSignatureInfos(IProject project, IReadOnlyList<DerivedTimeSignature> timeSignatures)
    {
        var tempo = project.TempoManager;
        var timeSig = project.TimeSignatureManager;
        return timeSignatures.OrderBy(t => t.Time)
            .Select(t => new TimeSignatureInfo
            {
                BarIndex = Math.Max(0, timeSig.GetBarAndBeatIndexForTick(tempo.GetTick(t.Time)).Item1),
                Numerator = t.Numerator,
                Denominator = t.Denominator,
            })
            .ToList();
    }
}
