using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NAudio.Midi;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.Science;

namespace TuneLab.Extensions.Formats.Midi;

[ImportFormat("mid")]
internal class MidiWithExtension_mid : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        return MidiUtility.Deserialize(stream);
    }
}

[ImportFormat("midi")]
internal class MidiWithExtension_midi : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        return MidiUtility.Deserialize(stream);
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
                else if (e is TextEvent le && le.MetaEventType == MetaEventType.Lyric)
                {
                    if (le.MetaEventType == MetaEventType.Lyric)
                        lyrics.Add(toTuneLabTick(le.AbsoluteTime), Encoding.UTF8.GetString(le.Data));
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
                    int barIndex = (int)(se.AbsoluteTime - lastTimeSignaturePos) / midi.DeltaTicksPerQuarterNote / 4 * denominator / numerator + lastTimeSignatureBarIndex;
                    info.TimeSignatures.Add(new TimeSignatureInfo() { BarIndex = barIndex, Numerator = numerator, Denominator = denominator });
                    lastTimeSignaturePos = se.AbsoluteTime;
                    lastTimeSignatureBarIndex = barIndex;
                }
            }
            for (int j = notes.Count - 1; j > 0; j--)
            {
                notes[j - 1].Dur = Math.Min(notes[j - 1].Dur, notes[j].Pos - notes[j - 1].Pos);
                if (notes[j - 1].Dur <= 0)
                    notes.RemoveAt(j - 1);
            }
            if (notes.Count != 0)
            {
                part.Dur = ((int)Math.Ceiling((double)notes.Last().EndPos() / (4 * MusicTheory.RESOLUTION)) + 1) * MusicTheory.RESOLUTION * 4;
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
                        note.Lyric = lastNoteEndPos == note.Pos ? "-" : "a";
                    }
                    lastNoteEndPos = note.EndPos();
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
}
