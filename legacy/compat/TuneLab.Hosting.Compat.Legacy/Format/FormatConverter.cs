using System;
using System.Collections.Generic;
using System.Linq;
using Old = TuneLab.Extensions.Formats.DataInfo;
using New = TuneLab.SDK;
using LStruct = TuneLab.Base.Structures;
using PStruct = TuneLab.Foundation;
using TuneLab.Hosting.Compat.Legacy.Conversion;

namespace TuneLab.Hosting.Compat.Legacy.Format;

// 工程模型 DataInfo 跨代深拷贝（DTO eager 深拷贝、转移所有权、无别名；冷路径）。
// Legacy（TuneLab.Extensions.Formats.DataInfo）↔ V1（TuneLab.SDK）。字段两代逐一对应；
// V1 无 Effects，故 MidiPart 不涉及 Effects。
internal static class FormatConverter
{
    // ── ProjectInfo ──
    public static New.ProjectInfo ToV1(this Old.ProjectInfo o) => new()
    {
        EditorInfo = o.EditorInfo.ToV1(),
        Tempos = o.Tempos.Select(ToV1).ToList(),
        TimeSignatures = o.TimeSignatures.Select(ToV1).ToList(),
        Tracks = o.Tracks.Select(ToV1).ToList(),
        ExportConfig = o.ExportConfig.ToV1(),
    };
    public static Old.ProjectInfo ToLegacy(this New.ProjectInfo n) => new()
    {
        EditorInfo = n.EditorInfo.ToLegacy(),
        Tempos = n.Tempos.Select(ToLegacy).ToList(),
        TimeSignatures = n.TimeSignatures.Select(ToLegacy).ToList(),
        Tracks = n.Tracks.Select(ToLegacy).ToList(),
        ExportConfig = n.ExportConfig.ToLegacy(),
    };

    // ── EditorInfo ──
    public static New.EditorInfo ToV1(this Old.EditorInfo o) => new() { PlayheadPos = o.PlayheadPos };
    public static Old.EditorInfo ToLegacy(this New.EditorInfo n) => new() { PlayheadPos = n.PlayheadPos };

    // ── ExportConfigInfo ──
    public static New.ExportConfigInfo ToV1(this Old.ExportConfigInfo o) => new()
    {
        ExportPath = o.ExportPath,
        FileName = o.FileName,
        SampleRate = o.SampleRate,
        BitDepth = o.BitDepth,
        MasterExportEnabled = o.MasterExportEnabled,
        MasterExportChannels = o.MasterExportChannels,
    };
    public static Old.ExportConfigInfo ToLegacy(this New.ExportConfigInfo n) => new()
    {
        ExportPath = n.ExportPath,
        FileName = n.FileName,
        SampleRate = n.SampleRate,
        BitDepth = n.BitDepth,
        MasterExportEnabled = n.MasterExportEnabled,
        MasterExportChannels = n.MasterExportChannels,
    };

    // ── TempoInfo ──
    public static New.TempoInfo ToV1(this Old.TempoInfo o) => new() { Pos = o.Pos, Bpm = o.Bpm };
    public static Old.TempoInfo ToLegacy(this New.TempoInfo n) => new() { Pos = n.Pos, Bpm = n.Bpm };

    // ── TimeSignatureInfo ──
    public static New.TimeSignatureInfo ToV1(this Old.TimeSignatureInfo o) => new()
    {
        BarIndex = o.BarIndex, Numerator = o.Numerator, Denominator = o.Denominator,
    };
    public static Old.TimeSignatureInfo ToLegacy(this New.TimeSignatureInfo n) => new()
    {
        BarIndex = n.BarIndex, Numerator = n.Numerator, Denominator = n.Denominator,
    };

    // ── TrackInfo ──
    public static New.TrackInfo ToV1(this Old.TrackInfo o) => new()
    {
        Name = o.Name, Gain = o.Gain, Pan = o.Pan, Mute = o.Mute, Solo = o.Solo,
        AsRefer = o.AsRefer, Color = o.Color,
        ExportEnabled = o.ExportEnabled, ExportChannels = o.ExportChannels,
        Parts = o.Parts.Select(ToV1).ToList(),
    };
    public static Old.TrackInfo ToLegacy(this New.TrackInfo n) => new()
    {
        Name = n.Name, Gain = n.Gain, Pan = n.Pan, Mute = n.Mute, Solo = n.Solo,
        AsRefer = n.AsRefer, Color = n.Color,
        ExportEnabled = n.ExportEnabled, ExportChannels = n.ExportChannels,
        Parts = n.Parts.Select(ToLegacy).ToList(),
    };

    // ── PartInfo（多态） ──
    public static New.PartInfo ToV1(this Old.PartInfo o) => o switch
    {
        Old.MidiPartInfo m => m.ToV1(),
        Old.AudioPartInfo a => a.ToV1(),
        _ => throw new NotSupportedException($"未知 PartInfo 类型: {o.GetType().FullName}"),
    };
    public static Old.PartInfo ToLegacy(this New.PartInfo n) => n switch
    {
        New.MidiPartInfo m => m.ToLegacy(),
        New.AudioPartInfo a => a.ToLegacy(),
        _ => throw new NotSupportedException($"未知 PartInfo 类型: {n.GetType().FullName}"),
    };

    // ── MidiPartInfo ──
    public static New.MidiPartInfo ToV1(this Old.MidiPartInfo o) => new()
    {
        Name = o.Name, Pos = o.Pos, Dur = o.Dur, Gain = o.Gain,
        SoundSource = o.Voice.ToSoundSource(),
        Notes = o.Notes.Select(ToV1).ToList(),
        Automations = o.Automations.ToV1Map(a => a.ToV1()),
        Pitch = o.Pitch.Select(p => p.ToV1()).ToList(),
        Vibratos = o.Vibratos.Select(ToV1).ToList(),
        Properties = o.Properties.ToV1(),
    };
    public static Old.MidiPartInfo ToLegacy(this New.MidiPartInfo n) => new()
    {
        Name = n.Name, Pos = n.Pos, Dur = n.Dur, Gain = n.Gain,
        Voice = n.SoundSource.ToLegacy(),
        Notes = n.Notes.Select(ToLegacy).ToList(),
        Automations = n.Automations.ToLegacyMap(a => a.ToLegacy()),
        Pitch = n.Pitch.Select(p => p.ToLegacy()).ToList(),
        Vibratos = n.Vibratos.Select(ToLegacy).ToList(),
        Properties = n.Properties.ToLegacy(),
    };

    // ── AudioPartInfo ──
    public static New.AudioPartInfo ToV1(this Old.AudioPartInfo o) => new()
    {
        Name = o.Name, Pos = o.Pos, Dur = o.Dur, Path = o.Path,
    };
    public static Old.AudioPartInfo ToLegacy(this New.AudioPartInfo n) => new()
    {
        Name = n.Name, Pos = n.Pos, Dur = n.Dur, Path = n.Path,
    };

    // ── NoteInfo ──
    public static New.NoteInfo ToV1(this Old.NoteInfo o) => new()
    {
        Pos = o.Pos, Dur = o.Dur, Pitch = o.Pitch, Lyric = o.Lyric, Pronunciation = o.Pronunciation,
        Properties = o.Properties.ToV1(),
        Phonemes = PhonemesToV1(o.Phonemes),
    };
    public static Old.NoteInfo ToLegacy(this New.NoteInfo n) => new()
    {
        Pos = n.Pos, Dur = n.Dur, Pitch = n.Pitch, Lyric = n.Lyric, Pronunciation = n.Pronunciation,
        Properties = n.Properties.ToLegacy(),
        Phonemes = PhonemesToLegacy(n.Phonemes),
    };

    // ── PhonemeInfo（note 级整组转换）──
    // 新模型 PhonemeInfo 用「时长 + 权重」(Duration/Weight)、位置由布局派生；旧模型是相对音符头的秒 StartTime/EndTime（音符头=0）。
    // 旧→新：时长 = EndTime − StartTime（音素连续）；旧模型无前置 / 弹性概念：
    //   按区间中点落在音符头之前（(Start+End)/2 < 0）判定为前置辅音（IsLead）；
    //   权重一律 0——老版本所有音素随音符等比缩放，布局的「全 w=0 退化为按原长等比」恰好复刻这一行为。
    static List<New.PhonemeInfo> PhonemesToV1(IReadOnlyList<Old.PhonemeInfo> phonemes)
    {
        var result = new List<New.PhonemeInfo>(phonemes.Count);
        foreach (var p in phonemes)
        {
            bool isLead = (p.StartTime + p.EndTime) < 0;
            double weight = 0;
            // 位置（起点）不入存储——由新模型布局按「核起点 = 音符头、前置往左、核填充」派生；此处仅留时长 = End − Start。
            result.Add(new New.PhonemeInfo
            {
                Symbol = p.Symbol,
                Duration = Math.Max(0, p.EndTime - p.StartTime),
                StretchWeight = weight,
                IsLead = isLead,
            });
        }
        return result;
    }

    // 新→旧为 best-effort：旧插件只认绝对相对秒，而 compat 这层无 note 几何（满末/邻居）还原元音填充与前辅音越界。
    // 退化为从 0 起累积各音素时长（元音亦按记录时长排、不填充）——格式转换够用。
    static List<Old.PhonemeInfo> PhonemesToLegacy(IReadOnlyList<New.PhonemeInfo> phonemes)
    {
        var result = new List<Old.PhonemeInfo>(phonemes.Count);
        double t = 0;
        foreach (var p in phonemes)
        {
            double end = t + System.Math.Max(0, p.Duration);
            result.Add(new Old.PhonemeInfo { Symbol = p.Symbol, StartTime = t, EndTime = end });
            t = end;
        }
        return result;
    }

    // ── VibratoInfo ──
    public static New.VibratoInfo ToV1(this Old.VibratoInfo o) => new()
    {
        Pos = o.Pos, Dur = o.Dur, Frequency = o.Frequency, Phase = o.Phase,
        Amplitude = o.Amplitude, Attack = o.Attack, Release = o.Release,
        AffectedAutomations = o.AffectedAutomations.ToV1Map(d => d),
    };
    public static Old.VibratoInfo ToLegacy(this New.VibratoInfo n) => new()
    {
        Pos = n.Pos, Dur = n.Dur, Frequency = n.Frequency, Phase = n.Phase,
        Amplitude = n.Amplitude, Attack = n.Attack, Release = n.Release,
        AffectedAutomations = n.AffectedAutomations.ToLegacyMap(d => d),
    };

    // ── VoiceInfo（legacy，仅 voice）↔ SoundSourceInfo（new）──
    // legacy 格式只有 voice：old→new 种类恒 Voice；new→old 丢弃 Kind（instrument part 无法回灌 legacy 格式）。
    public static New.SoundSourceInfo ToSoundSource(this Old.VoiceInfo o) => new() { Kind = New.SourceKind.Voice, Type = o.Type, ID = o.ID };
    public static Old.VoiceInfo ToLegacy(this New.SoundSourceInfo n) => new() { Type = n.Type, ID = n.ID };

    // ── AutomationInfo ──
    public static New.AutomationInfo ToV1(this Old.AutomationInfo o) => new()
    {
        DefaultValue = o.DefaultValue,
        Points = o.Points.Select(p => p.ToV1()).ToList(),
    };
    public static Old.AutomationInfo ToLegacy(this New.AutomationInfo n) => new()
    {
        DefaultValue = n.DefaultValue,
        Points = n.Points.Select(p => p.ToLegacy()).ToList(),
    };

    // ── Map<string, T> 辅助 ──
    static PStruct.Map<string, TNew> ToV1Map<TOld, TNew>(this LStruct.Map<string, TOld> old, Func<TOld, TNew> conv)
    {
        var m = new PStruct.Map<string, TNew>();
        foreach (var kv in old)
            m[kv.Key] = conv(kv.Value);
        return m;
    }
    static LStruct.Map<string, TOld> ToLegacyMap<TNew, TOld>(this PStruct.Map<string, TNew> neo, Func<TNew, TOld> conv)
    {
        var m = new LStruct.Map<string, TOld>();
        foreach (var kv in neo)
            m[kv.Key] = conv(kv.Value);
        return m;
    }
}
