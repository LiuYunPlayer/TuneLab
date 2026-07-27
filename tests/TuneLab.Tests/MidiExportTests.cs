using System.Text;
using TuneLab.Extensions.Formats.Midi;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// MIDI 导出（`MidiUtility.Serialize`）。重点是【导出 → 再导入】的往返保真：MIDI 是有损落差格式，
// 能表达的那部分（tempo / 拍号 / 音符 / 歌词 / 轨名）必须一比一回来，不能悄悄走形。
// 中文歌词单列一测——NAudio 自己的 TextEvent 会把 "好" 写成单字节 0x7D，故本实现手写 UTF8，这条是它的封条。
public class MidiExportTests
{
    const int PPQ = MusicTheory.RESOLUTION;   // 480

    static MidiPartInfo Part(double pos, double endOffset, string name, params (double pos, double dur, int pitch, string lyric)[] notes)
    {
        var part = new MidiPartInfo { Name = name, Pos = pos, StartOffset = 0, EndOffset = endOffset };
        foreach (var n in notes)
            part.Notes.Add(new NoteInfo { Pos = n.pos, Dur = n.dur, Pitch = n.pitch, Lyric = n.lyric });
        return part;
    }

    static ProjectInfo RoundTrip(ProjectInfo source)
    {
        using var stream = new MemoryStream();
        MidiUtility.Serialize(stream, source);
        stream.Position = 0;
        return MidiUtility.Deserialize(stream);
    }

    [Fact]
    public void RoundTrip_NotesAndLyrics()
    {
        var source = new ProjectInfo();
        source.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        var track = new TrackInfo { Name = "Lead" };
        track.Parts.Add(Part(0, 4 * PPQ, "p1",
            (0, PPQ, 60, "do"),
            (PPQ, PPQ, 62, "re"),
            (2 * PPQ, PPQ, 64, "mi")));
        source.Tracks.Add(track);

        var result = RoundTrip(source);

        Assert.Single(result.Tracks);
        var notes = ((MidiPartInfo)result.Tracks[0].Parts[0]).Notes;
        Assert.Equal(3, notes.Count);
        Assert.Equal([0, PPQ, 2 * PPQ], notes.Select(n => n.Pos));
        Assert.Equal([PPQ, PPQ, PPQ], notes.Select(n => n.Dur));
        Assert.Equal([60, 62, 64], notes.Select(n => n.Pitch));
        Assert.Equal(["do", "re", "mi"], notes.Select(n => n.Lyric));
    }

    [Fact]
    public void RoundTrip_ChineseLyricsAndTrackName_SurviveAsUtf8()
    {
        // 封条：NAudio 的 TextEvent 会把非 ASCII 截成低字节（"好" → 0x7D）。手写 UTF8 才能往返。
        var source = new ProjectInfo();
        var track = new TrackInfo { Name = "主唱轨" };
        track.Parts.Add(Part(0, 2 * PPQ, "段落一", (0, PPQ, 60, "好"), (PPQ, PPQ, 62, "世界")));
        source.Tracks.Add(track);

        var result = RoundTrip(source);

        Assert.Equal("主唱轨", result.Tracks[0].Name);
        var notes = ((MidiPartInfo)result.Tracks[0].Parts[0]).Notes;
        Assert.Equal(["好", "世界"], notes.Select(n => n.Lyric));
    }

    [Fact]
    public void RoundTrip_TemposPreserved()
    {
        var source = new ProjectInfo();
        source.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        source.Tempos.Add(new TempoInfo { Pos = 4 * PPQ, Bpm = 90 });
        source.Tempos.Add(new TempoInfo { Pos = 8 * PPQ, Bpm = 145 });

        var result = RoundTrip(source);

        Assert.Equal([0, 4 * PPQ, 8 * PPQ], result.Tempos.Select(t => t.Pos));
        // BPM 经 microseconds-per-quarter 取整往返，容许极小误差（整数微秒的量化）。
        Assert.Equal([120.0, 90.0, 145.0], result.Tempos.Select(t => Math.Round(t.Bpm, 3)));
    }

    [Fact]
    public void RoundTrip_TimeSignatures_NonFourFour()
    {
        // 回归锚：导入侧的 barIndex 折算原先逐级整数除、且拿新拍号去量旧区段，非 4/4 一律错位
        // （3/4 下 2 小节 = 2880 tick 被算成 barIndex 1）。这里 3/4 起、第 2 小节转 7/8。
        var source = new ProjectInfo();
        source.TimeSignatures.Add(new TimeSignatureInfo { BarIndex = 0, Numerator = 3, Denominator = 4 });
        source.TimeSignatures.Add(new TimeSignatureInfo { BarIndex = 2, Numerator = 7, Denominator = 8 });
        source.TimeSignatures.Add(new TimeSignatureInfo { BarIndex = 5, Numerator = 4, Denominator = 4 });

        var result = RoundTrip(source);

        Assert.Equal([0, 2, 5], result.TimeSignatures.Select(t => t.BarIndex));
        Assert.Equal([3, 7, 4], result.TimeSignatures.Select(t => t.Numerator));
        Assert.Equal([4, 8, 4], result.TimeSignatures.Select(t => t.Denominator));
    }

    [Fact]
    public void PartPos_IsFoldedIntoAbsoluteTick()
    {
        // NoteInfo.Pos 是 part 相对，MIDI 只认绝对——part 锚点必须折进去。
        var source = new ProjectInfo();
        var track = new TrackInfo { Name = "t" };
        track.Parts.Add(Part(8 * PPQ, 2 * PPQ, "moved", (0, PPQ, 60, "a"), (PPQ, PPQ, 61, "b")));
        source.Tracks.Add(track);

        var result = RoundTrip(source);

        // 导入侧的 part 锚点恒为 0，故绝对位置直接落在 note.Pos 上。
        var notes = ((MidiPartInfo)result.Tracks[0].Parts[0]).Notes;
        Assert.Equal([8 * PPQ, 9 * PPQ], notes.Select(n => n.Pos));
    }

    [Fact]
    public void MultipleParts_MergeIntoOneTrack()
    {
        var source = new ProjectInfo();
        var track = new TrackInfo { Name = "t" };
        track.Parts.Add(Part(0, 2 * PPQ, "p1", (0, PPQ, 60, "a")));
        track.Parts.Add(Part(4 * PPQ, 2 * PPQ, "p2", (0, PPQ, 67, "b")));
        source.Tracks.Add(track);

        var result = RoundTrip(source);

        // 一条 TuneLab 轨 → 一条 MIDI 轨：两个 part 的音符合并、按绝对 tick 落位。
        Assert.Single(result.Tracks);
        var notes = ((MidiPartInfo)result.Tracks[0].Parts[0]).Notes;
        Assert.Equal(2, notes.Count);
        Assert.Equal([0, 4 * PPQ], notes.Select(n => n.Pos));
        Assert.Equal([60, 67], notes.Select(n => n.Pitch));
    }

    [Fact]
    public void ClippedNotes_AreNotExported()
    {
        // part 可见区间 = [Pos+StartOffset, Pos+EndOffset)：裁掉的内容不写出，否则往返会凭空多出
        // 用户已经裁掉的音符（MIDI 没有"存在但不可见"的概念）。判据取音符起点。
        var source = new ProjectInfo();
        var track = new TrackInfo { Name = "t" };
        var part = new MidiPartInfo { Name = "p", Pos = 0, StartOffset = PPQ, EndOffset = 3 * PPQ };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = PPQ, Pitch = 60, Lyric = "before" });          // 起点在左裁线之前
        part.Notes.Add(new NoteInfo { Pos = PPQ, Dur = PPQ, Pitch = 61, Lyric = "in" });            // 区间内
        part.Notes.Add(new NoteInfo { Pos = 2 * PPQ, Dur = PPQ, Pitch = 62, Lyric = "also-in" });   // 区间内
        part.Notes.Add(new NoteInfo { Pos = 3 * PPQ, Dur = PPQ, Pitch = 63, Lyric = "after" });     // 起点即右裁线（右开）
        track.Parts.Add(part);
        source.Tracks.Add(track);

        var result = RoundTrip(source);

        var notes = ((MidiPartInfo)result.Tracks[0].Parts[0]).Notes;
        Assert.Equal(["in", "also-in"], notes.Select(n => n.Lyric));
    }

    [Fact]
    public void NonPositiveDuration_IsDropped()
    {
        var source = new ProjectInfo();
        var track = new TrackInfo { Name = "t" };
        var part = new MidiPartInfo { Name = "p", Pos = 0, EndOffset = 4 * PPQ };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 0, Pitch = 60, Lyric = "zero" });
        part.Notes.Add(new NoteInfo { Pos = PPQ, Dur = -PPQ, Pitch = 61, Lyric = "negative" });
        part.Notes.Add(new NoteInfo { Pos = 2 * PPQ, Dur = PPQ, Pitch = 62, Lyric = "ok" });
        track.Parts.Add(part);
        source.Tracks.Add(track);

        var result = RoundTrip(source);

        var notes = ((MidiPartInfo)result.Tracks[0].Parts[0]).Notes;
        Assert.Equal(["ok"], notes.Select(n => n.Lyric));
    }

    [Fact]
    public void AudioPart_IsSkipped()
    {
        // MIDI 里没有音频轨的位置：audio part 如实丢弃，且不因此产出一条空轨。
        var source = new ProjectInfo();
        var track = new TrackInfo { Name = "audio only" };
        track.Parts.Add(new AudioPartInfo { Name = "wav", Pos = 0, EndOffset = 4 * PPQ, Path = "x.wav" });
        source.Tracks.Add(track);

        var result = RoundTrip(source);

        Assert.Empty(result.Tracks);
    }

    [Fact]
    public void EmptyProject_ProducesReadableFile()
    {
        var result = RoundTrip(new ProjectInfo());

        Assert.Empty(result.Tracks);
        Assert.Empty(result.Tempos);
    }

    [Fact]
    public void Serialize_HonorsStreamContract_NoSeekNoDispose()
    {
        // IExportFormat 契约：宿主拥有流，插件只从当前位置顺序写，不得 Seek / Dispose / 改 Position。
        // 用一个禁止这些动作的包装流做机械验证（BinaryWriter 默认会在 Dispose 时连带关闭底层流——
        // 故实现里必须传 leaveOpen:true，这条断言就是那件事的封条）。
        var source = new ProjectInfo();
        var track = new TrackInfo { Name = "t" };
        track.Parts.Add(Part(0, 2 * PPQ, "p", (0, PPQ, 60, "a")));
        source.Tracks.Add(track);

        using var inner = new MemoryStream();
        var guarded = new WriteOnlyGuardStream(inner);
        MidiUtility.Serialize(guarded, source);

        Assert.False(guarded.WasDisposed);
        Assert.False(guarded.WasSeeked);
        Assert.True(inner.Length > 0);
    }

    // 只允许顺序写的流：Seek / SetLength / set_Position / Dispose 一律视为违约。
    sealed class WriteOnlyGuardStream(Stream inner) : Stream
    {
        public bool WasDisposed { get; private set; }
        public bool WasSeeked { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set { WasSeeked = true; throw new NotSupportedException("stream contract: Position must not be set."); }
        }

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Flush() => inner.Flush();

        public override long Seek(long offset, SeekOrigin origin)
        {
            WasSeeked = true;
            throw new NotSupportedException("stream contract: Seek is not allowed.");
        }

        public override void SetLength(long value) => throw new NotSupportedException("stream contract: SetLength is not allowed.");
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            // 刻意不向下传递：底层流的生命周期归宿主。
        }
    }
}
