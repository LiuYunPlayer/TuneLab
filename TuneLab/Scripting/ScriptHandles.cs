using System.Globalization;
using System.Linq;
using TuneLab.Data;
using TuneLab.Foundation;

namespace TuneLab.Scripting;

// 脚本面向的「句柄」：包装一个数据层对象（轨/part/note），只暴露安全的只读数据属性给脚本语言。
// 所有改动经 ScriptProjectApi 的方法、传句柄进行——句柄本身不带改动方法，避免脚本绕过宿主的
// merge/commit 收口。句柄是【临时】的：仅当次脚本运行有效，不可序列化、脚本源码不得内嵌句柄值，
// 只能在脚本里 get 后即用（数据层对象无持久 id，重启即失效）。被删除后再用会被宿主拦下报错。
//
// 坐标：对外暴露的位置/时长一律【绝对（全局）tick】——note.pos 已加回所属 part 起点。属性实时读底层
// 数据对象，故脚本改完再读句柄属性即见最新值。

// 一个音符句柄。
internal sealed class ScriptNote(INote note)
{
    internal INote Note { get; } = note;
    internal bool Removed { get; set; }

    public double Pos => Note.GlobalStartPos();              // 绝对 tick
    public double Dur => Note.Dur.Value;
    public int Pitch => Note.Pitch.Value;                    // MIDI 0..127
    public string PitchName => ScriptPitch.Name(Note.Pitch.Value);
    public string Lyric => Note.Lyric.Value;

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Note(pos={0:0}, dur={1:0}, pitch={2}/{3}, lyric=\"{4}\")",
            Pos, Dur, Pitch, PitchName, Lyric);
}

// 一个 part 句柄（midi 或 audio）。音符/曲线只对 midi part 有效。
internal sealed class ScriptPart(IPart part)
{
    internal IPart Part { get; } = part;
    internal bool Removed { get; set; }

    public string Name => Part.Name.Value;
    public double Pos => Part.Pos.Value;                     // 绝对 tick
    public double Dur => Part.Dur.Value;
    public bool IsMidi => Part is IMidiPart;
    public string Type => Part is IMidiPart ? "midi" : "audio";
    public string Voice => Part is IMidiPart midi ? midi.Voice.Name : "";
    public int NoteCount => Part is IMidiPart midi ? midi.Notes.Count() : 0;

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Part(\"{0}\", {1}, ticks [{2:0}..{3:0}])",
            Name, Type, Pos, Pos + Dur);
}

// 一个轨道句柄。
internal sealed class ScriptTrack(ITrack track)
{
    internal ITrack Track { get; } = track;
    internal bool Removed { get; set; }

    public string Name => Track.Name.Value;
    public bool Mute => Track.IsMute.Value;
    public bool Solo => Track.IsSolo.Value;
    public double GainDb => Track.Gain.Value;
    public double Pan => Track.Pan.Value;
    public int PartCount => Track.Parts.Count();

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Track(\"{0}\", parts={1})", Name, PartCount);
}

// 一个速度标记（只读快照）。
internal sealed class ScriptTempo(double bpm, double tick)
{
    public double Bpm { get; } = bpm;
    public double Tick { get; } = tick;
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Tempo({0:0.##}bpm@{1:0})", Bpm, Tick);
}

// 一个拍号标记（只读快照）；Bar 为 1-based 小节号。
internal sealed class ScriptTimeSignature(int numerator, int denominator, int bar)
{
    public int Numerator { get; } = numerator;
    public int Denominator { get; } = denominator;
    public int Bar { get; } = bar;
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "TimeSig({0}/{1}@bar{2})", Numerator, Denominator, Bar);
}

// 播放线位置（只读快照）。
internal sealed class ScriptPlayhead(double tick, double seconds, int bar, double beat, bool playing)
{
    public double Tick { get; } = tick;
    public double Seconds { get; } = seconds;
    public int Bar { get; } = bar;          // 1-based
    public double Beat { get; } = beat;     // 1-based
    public bool Playing { get; } = playing;
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Playhead(tick={0:0}, bar {1}:{2:0.##}, playing={3})", Tick, Bar, Beat, Playing);
}

// 音高 MIDI → 音名（C0 = MIDI 12）。
internal static class ScriptPitch
{
    static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static string Name(int midi)
    {
        int rel = midi - MusicTheory.C0_PITCH;
        int octave = (int)System.Math.Floor(rel / 12.0);
        int idx = ((rel % 12) + 12) % 12;
        return Names[idx] + octave.ToString(CultureInfo.InvariantCulture);
    }
}
