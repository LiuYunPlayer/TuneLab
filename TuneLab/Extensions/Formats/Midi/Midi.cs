using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Midi;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Formats.Midi;

internal class MidiWithExtension_mid : IImportFormat, IExportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        return MidiUtility.Deserialize(stream);
    }

    public void Serialize(Stream output, ProjectInfo info)
    {
        MidiUtility.Serialize(output, info);
    }
}

internal class MidiWithExtension_midi : IImportFormat, IExportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        return MidiUtility.Deserialize(stream);
    }

    public void Serialize(Stream output, ProjectInfo info)
    {
        MidiUtility.Serialize(output, info);
    }
}

internal static class MidiUtility
{
    public static ProjectInfo Deserialize(Stream stream)
    {
        var info = new ProjectInfo();

        var midi = new MidiFile(stream, true);
        var toTuneLabTick = (double midiTick) => midiTick * MusicTheory.RESOLUTION / midi.DeltaTicksPerQuarterNote;
        for (int i = 0; i < midi.Tracks; i++)
        {
            var part = new MidiPartInfo();
            var notes = part.Notes;
            var lyrics = new Dictionary<double, string>();
            long lastTimeSignaturePos = 0;
            int lastTimeSignatureBarIndex = 0;
            int lastTicksPerBar = 4 * midi.DeltaTicksPerQuarterNote;   // 首个拍号事件之前按 MIDI 的隐含 4/4 计
            foreach (var e in midi.Events.GetTrackEvents(i))
            {
                if (e is NoteOnEvent ne)
                {
                    if (ne.OffEvent == null)
                        continue;

                    var note = new NoteInfo
                    {
                        Pos = toTuneLabTick(ne.AbsoluteTime),
                        Dur = toTuneLabTick(ne.NoteLength),
                        Pitch = ne.NoteNumber,
                    };
                    notes.Add(note);
                }
                // 外层只判类型、具体 meta 类型在内层分派：原先外层就锁死了 MetaEventType.Lyric，
                // 使 SequenceTrackName 那一支永不可达——轨名从来没被读进来过（导出侧写了轨名，往返却读不回）。
                else if (e is TextEvent le)
                {
                    if (le.MetaEventType == MetaEventType.Lyric)
                        // 容错畸形 MIDI：同一 tick 可能有多个 Lyric 事件（Dictionary.Add 会抛「duplicate key」）。
                        // 用 TryAdd 取先到、忽略后续重复——歌词按 tick 查回音符（见下），一个 tick 只需一条。
                        lyrics.TryAdd(toTuneLabTick(le.AbsoluteTime), Encoding.UTF8.GetString(le.Data));
                    else if (le.MetaEventType == MetaEventType.SequenceTrackName)
                        part.Name = Encoding.UTF8.GetString(le.Data);
                }
                else if (e is TempoEvent te)
                {
                    info.Tempos.Add(new TempoInfo() { Pos = (int)(te.AbsoluteTime * MusicTheory.RESOLUTION / midi.DeltaTicksPerQuarterNote), Bpm = 60000000.0 / te.MicrosecondsPerQuarterNote });
                }
                else if (e is TimeSignatureEvent se)
                {
                    int numerator = se.Numerator;
                    int denominator = (int)Math.Pow(2, se.Denominator);
                    // 这段距离由【上一个】拍号统治，故按它的每小节 tick 折算，而不是按当前这个新拍号的；
                    // 且必须一次除到底、不逐级整数除——原式 (t/ppq/4*den/num) 会过早截断：3/4 下 2880 tick
                    // 明明是 2 小节，却算成 2880/480/4*4/3 = 1。两处都只在"拍号从未变过"时才恰好正确。
                    int barIndex = lastTimeSignatureBarIndex + (int)((se.AbsoluteTime - lastTimeSignaturePos) / lastTicksPerBar);
                    info.TimeSignatures.Add(new TimeSignatureInfo() { BarIndex = barIndex, Numerator = numerator, Denominator = denominator });
                    lastTimeSignaturePos = se.AbsoluteTime;
                    lastTimeSignatureBarIndex = barIndex;
                    lastTicksPerBar = 4 * midi.DeltaTicksPerQuarterNote * numerator / denominator;
                }
            }
            // 忠实保留同时发声（和弦/重叠）：不再把每个 note 的尾巴钳到下一 note 起点。
            // 仅剔除无效的零/负时长 note（MIDI NoteLength 可能为 0）。去重叠下放编辑器/合成侧决定。
            for (int j = notes.Count - 1; j >= 0; j--)
            {
                if (notes[j].Dur <= 0)
                    notes.RemoveAt(j);
            }
            if (notes.Count != 0)
            {
                var lastNote = notes.Last();
                part.EndOffset = ((int)Math.Ceiling((lastNote.Pos + lastNote.Dur) / (4 * MusicTheory.RESOLUTION)) + 1) * MusicTheory.RESOLUTION * 4;
                var track = new TrackInfo();
                track.Name = part.Name;
                track.Parts.Add(part);
                info.Tracks.Add(track);
                double lastNoteEndPos = notes.First().Pos - 1;
                for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
                {
                    var note = notes[noteIndex];
                    if (lyrics.TryGetValue(note.Pos, out var lyric))
                    {
                        note.Lyric = lyric;
                    }
                    else
                    {
                        note.Lyric = lastNoteEndPos == note.Pos ? "-" : "a";   // "-" 延音软约定（相邻 note 默认）
                    }
                    lastNoteEndPos = note.Pos + note.Dur;
                }
                if (string.IsNullOrEmpty(part.Name))
                {
                    part.Name = "Part_1";
                }
                if (string.IsNullOrEmpty(track.Name))
                {
                    track.Name = "MidiTrack_" + info.Tracks.Count;
                }
            }
        }

        return info;
    }

    // ——导出（SMF format 1，division = RESOLUTION 故 tick 一比一、无换算损失）——
    //
    // 【为什么手写字节，不用 NAudio 的事件类 + MidiFile.Export】两条硬理由：
    // ① MidiFile.Export 只有 filename 重载，而 IExportFormat 契约给的是【宿主的流】且明禁 Seek/Dispose；
    // ② NAudio 的 TextEvent 把字符串按单字节写出——"好" 会变成 FF 05 01 7D（只剩低字节），中文歌词与
    //    轨名当场毁掉，而导入侧读的是 Encoding.UTF8.GetString(le.Data)。故文本 meta 必须自己写 UTF8。
    // 文本既已须手写，索性全部事件一处编码，免得两套混用、各有各的怪癖。
    //
    // 【MIDI 承载不了、故如实丢弃的】音源 / 自动化曲线 / 音高偏差 / 颤音 / 音素 / effect 链 / 增益，
    // 以及 audio part（MIDI 里没有音频轨的位置）。这是格式固有落差，不是实现偷懒。
    public static void Serialize(Stream output, ProjectInfo info)
    {
        var tracks = new List<byte[]> { BuildConductorTrack(info) };
        foreach (var trackInfo in info.Tracks)
            tracks.Add(BuildNoteTrack(trackInfo));

        // 契约要求只顺序写：故各 track 先各自成型于 MemoryStream（自己的缓冲，长度可回填），
        // 再按 MThd → MTrk… 一次性顺序推给宿主的流。全程不 Seek output。
        // leaveOpen: true —— 流的生命周期归宿主，BinaryWriter 默认会在 Dispose 时连带关掉它。
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("MThd"));
        WriteBigEndian32(writer, 6);
        WriteBigEndian16(writer, 1);                        // format 1：track 0 = conductor，其后各轨并行
        WriteBigEndian16(writer, (ushort)tracks.Count);
        WriteBigEndian16(writer, MusicTheory.RESOLUTION);   // division = 自家 PPQ，导入侧换算即恒等
        foreach (var track in tracks)
        {
            writer.Write(Encoding.ASCII.GetBytes("MTrk"));
            WriteBigEndian32(writer, (uint)track.Length);
            writer.Write(track);
        }
        writer.Flush();
    }

    // conductor（track 0）：tempo map + 拍号。工程没有曲名字段，故不写 SequenceTrackName。
    static byte[] BuildConductorTrack(ProjectInfo info)
    {
        var events = new List<TimedEvent>();

        foreach (var tempo in info.Tempos)
        {
            int microsecondsPerQuarter = (int)Math.Round(60000000.0 / tempo.Bpm);
            events.Add(new TimedEvent(ToMidiTick(tempo.Pos), EventOrder.Meta, MetaBytes(0x51,
            [
                (byte)(microsecondsPerQuarter >> 16), (byte)(microsecondsPerQuarter >> 8), (byte)microsecondsPerQuarter,
            ])));
        }

        // TimeSignatureInfo 按【小节序号】定址，MIDI 按 tick——须按序累加折算（导入侧那段的反向）。
        long tick = 0;
        int lastBarIndex = 0;
        int lastTicksPerBar = 4 * MusicTheory.RESOLUTION;   // 无拍号时的默认 4/4
        foreach (var timeSignature in info.TimeSignatures.OrderBy(x => x.BarIndex))
        {
            tick += (long)(timeSignature.BarIndex - lastBarIndex) * lastTicksPerBar;
            lastBarIndex = timeSignature.BarIndex;
            lastTicksPerBar = 4 * MusicTheory.RESOLUTION * timeSignature.Numerator / timeSignature.Denominator;
            events.Add(new TimedEvent(tick, EventOrder.Meta, MetaBytes(0x58,
            [
                // dd 是【幂指数】而非分母本身（4 → 2），与导入侧的 Math.Pow(2, se.Denominator) 对偶。
                (byte)timeSignature.Numerator, (byte)Math.Round(Math.Log2(timeSignature.Denominator)),
                24,   // cc：每四分音符 24 MIDI clock（标准值）
                8,    // bb：每四分音符 8 个 32 分音符（标准值）
            ])));
        }

        return Assemble(events);
    }

    // 一条 TuneLab 轨 → 一条 MIDI 轨：轨名 + 该轨【所有 midi part】的音符按绝对 tick 合并。
    static byte[] BuildNoteTrack(TrackInfo trackInfo)
    {
        var events = new List<TimedEvent>();
        if (!string.IsNullOrEmpty(trackInfo.Name))
            events.Add(new TimedEvent(0, EventOrder.Meta, MetaBytes(0x03, Encoding.UTF8.GetBytes(trackInfo.Name))));

        foreach (var partInfo in trackInfo.Parts)
        {
            // audio part 无从表达，跳过（承载落差，见类型注释）。
            if (partInfo is not MidiPartInfo midiPart)
                continue;

            // part 的可见区间：越界内容【不导出】——MIDI 没有"存在但被裁掉"的概念，写出去再读回来
            // 就等于凭空多出用户已裁掉的内容。判据取音符【起点】落在区间内；命中则整音符写出、不截尾
            // （MIDI 音符本可跨任何边界，截尾反倒是篡改时长）。
            double partStart = partInfo.Pos + partInfo.StartOffset;
            double partEnd = partInfo.Pos + partInfo.EndOffset;
            foreach (var note in midiPart.Notes)
            {
                if (note.Dur <= 0)   // 与导入侧剔除 Dur <= 0 对称
                    continue;

                double absolutePos = partInfo.Pos + note.Pos;   // NoteInfo.Pos 是 part 相对，MIDI 要绝对
                if (absolutePos < partStart || absolutePos >= partEnd)
                    continue;

                long onTick = ToMidiTick(absolutePos);
                long offTick = ToMidiTick(absolutePos + note.Dur);
                if (offTick <= onTick)   // 四舍五入后塌成零长：给它最短的 1 tick，别写出 NoteOn 却无声
                    offTick = onTick + 1;

                // MIDI 音高只有 0..127。越界钳制而非丢弃——丢音符比移调更糟（且 UI 本就不会产生越界值）。
                int pitch = Math.Clamp(note.Pitch, 0, 127);
                if (!string.IsNullOrEmpty(note.Lyric))
                    events.Add(new TimedEvent(onTick, EventOrder.Meta, MetaBytes(0x05, Encoding.UTF8.GetBytes(note.Lyric))));
                events.Add(new TimedEvent(onTick, EventOrder.NoteOn, [0x90, (byte)pitch, DefaultVelocity]));
                events.Add(new TimedEvent(offTick, EventOrder.NoteOff, [0x80, (byte)pitch, 0]));
            }
        }

        return Assemble(events);
    }

    // 音符没有力度字段（NoteInfo 无 velocity，导入侧也忽略它）——给一个中性默认值。
    const byte DefaultVelocity = 100;

    // 同 tick 的落笔次序：先收音、再歌词、后起音。歌词压在起音之前是惯例；NoteOff 抢在 NoteOn 之前，
    // 免得同音高的"接吻音符"被解读成一开一关顺序颠倒。
    enum EventOrder { NoteOff = 0, Meta = 1, NoteOn = 2 }

    readonly record struct TimedEvent(long Tick, EventOrder Order, byte[] Payload);

    static long ToMidiTick(double tuneLabTick) => (long)Math.Round(tuneLabTick);

    static byte[] MetaBytes(byte type, byte[] data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0xFF);
        writer.Write(type);
        WriteVariableLength(writer, data.Length);
        writer.Write(data);
        writer.Flush();
        return stream.ToArray();
    }

    // 事件按 (tick, 次序) 排稳序 → delta time 编码 → 收尾 EndTrack。OrderBy 是稳定排序，故同 tick
    // 同档的多条（如一串歌词）保持加入顺序。
    static byte[] Assemble(List<TimedEvent> events)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        long lastTick = 0;
        foreach (var e in events.OrderBy(x => x.Tick).ThenBy(x => x.Order))
        {
            WriteVariableLength(writer, (int)(e.Tick - lastTick));
            writer.Write(e.Payload);
            lastTick = e.Tick;
        }
        WriteVariableLength(writer, 0);
        writer.Write(new byte[] { 0xFF, 0x2F, 0x00 });   // EndTrack：SMF 要求每条轨以此收尾
        writer.Flush();
        return stream.ToArray();
    }

    // MIDI 的可变长数量（7 位一组、高位为续标志），delta time 与 meta 长度都用它。
    static void WriteVariableLength(BinaryWriter writer, int value)
    {
        var bytes = new Stack<byte>();
        bytes.Push((byte)(value & 0x7F));
        value >>= 7;
        while (value > 0)
        {
            bytes.Push((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        foreach (var b in bytes)
            writer.Write(b);
    }

    // chunk 头的长度与 header 字段都是大端，而 BinaryWriter 是小端——手动摆字节序。
    static void WriteBigEndian32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    static void WriteBigEndian16(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }
}
