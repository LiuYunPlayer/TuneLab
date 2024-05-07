using Avalonia;
using DynamicData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI.Components;
using TuneLab.Data;
using TuneLab.Utils;
using TuneLab.Extensions.Voices;
using TuneLab.Base.Utils;

namespace TuneLab.Views;

internal partial class PianoGrid
{
    class PianoGridItem(PianoGrid pianoGrid) : Item
    {
        public PianoGrid PianoGrid => pianoGrid;
    }

    class NoteItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid)
    {
        public INote Note;

        public Rect Rect()
        {
            return PianoGrid.NoteRect(Note);
        }

        public override bool Raycast(Avalonia.Point point)
        {
            return Rect().Contains(point);
        }
    }

    class NoteStartResizeItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid)
    {
        public INote Note;

        public override bool Raycast(Avalonia.Point point)
        {
            double x = PianoGrid.TickAxis.Tick2X(Note.GlobalStartPos());
            double left = x - 8;
            double right = x + 8;
            var last = Note.Last;
            if (last != null && last.Pitch.Value == Note.Pitch.Value)
            {
                double lastX = PianoGrid.TickAxis.Tick2X(last.GlobalEndPos());
                left = x - Math.Min(8, Math.Abs(lastX - x));
            }
            double top = PianoGrid.PitchAxis.Pitch2Y(Note.Pitch.Value + 1);
            double bottom = PianoGrid.PitchAxis.Pitch2Y(Note.Pitch.Value);
            return point.Y >= top && point.Y <= bottom && point.X >= left && point.X <= right;
        }
    }

    class NoteEndResizeItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid)
    {
        public INote Note;

        public override bool Raycast(Avalonia.Point point)
        {
            double x = PianoGrid.TickAxis.Tick2X(Note.GlobalEndPos());
            double left = x - 8;
            double right = x + 8;
            double top = PianoGrid.PitchAxis.Pitch2Y(Note.Pitch.Value + 1);
            double bottom = PianoGrid.PitchAxis.Pitch2Y(Note.Pitch.Value);
            return point.Y >= top && point.Y <= bottom && point.X >= left && point.X <= right;
        }
    }

    interface IVibratoItem
    {
        Vibrato Vibrato { get; }
        PianoGrid PianoGrid { get; }

        public Point FrequencyPosition()
        {
            var (x, pitch) = PositionAndPitch();
            double y = double.IsNaN(pitch) ? double.NaN : PianoGrid.PitchAxis.Pitch2Y(pitch + Vibrato.Amplitude) - 24;
            return new Point(x, y);
        }

        public Point PhasePosition()
        {
            var (x, pitch) = PositionAndPitch();
            double y = double.IsNaN(pitch) ? double.NaN : PianoGrid.PitchAxis.Pitch2Y(pitch - Vibrato.Amplitude) + 24;
            return new Point(x, y);
        }

        (double, double) PositionAndPitch()
        {
            double x = PianoGrid.TickAxis.Tick2X(Vibrato.GlobalStartPos() + Vibrato.Dur / 2);
            double pitch = PianoGrid.Part == null ? double.NaN : PianoGrid.Part.Pitch.GetValue(Vibrato.Pos + Vibrato.Dur / 2) + 0.5;
            return (x, pitch);
        }
    }

    class VibratoItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid), IVibratoItem
    {
        public Vibrato Vibrato { get; set; }

        public override bool Raycast(Point point)
        {
            double left = PianoGrid.TickAxis.Tick2X(Vibrato.GlobalStartPos());
            double right = PianoGrid.TickAxis.Tick2X(Vibrato.GlobalEndPos());
            return point.X > left && point.X < right;
        }
    }

    class VibratoAmplitudeItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid), IVibratoItem
    {
        public Vibrato Vibrato { get; set; }

        public override bool Raycast(Point point)
        {
            double left = PianoGrid.TickAxis.Tick2X(Vibrato.GlobalStartPos());
            double right = PianoGrid.TickAxis.Tick2X(Vibrato.GlobalEndPos());
            double pitch = PianoGrid.Part == null ? double.NaN : PianoGrid.Part.Pitch.GetValue(Vibrato.Pos + Vibrato.Dur / 2);
            if (double.IsNaN(pitch))
                return false;

            double top = PianoGrid.PitchAxis.Pitch2Y(pitch + 0.5 + Vibrato.Amplitude) - 24;
            double bottom = PianoGrid.PitchAxis.Pitch2Y(pitch + 0.5 - Vibrato.Amplitude) + 24;
            return point.X > left && point.X < right && point.Y > top && point.Y < bottom;
        }
    }

    class VibratoStartResizeItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid), IVibratoItem
    {
        public Vibrato Vibrato { get; set; }

        public override bool Raycast(Point point)
        {
            double x = PianoGrid.TickAxis.Tick2X(Vibrato.GlobalStartPos());
            return point.X > x - 8 && point.X < x + 8;
        }
    }

    class VibratoEndResizeItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid), IVibratoItem
    {
        public Vibrato Vibrato { get; set; }

        public override bool Raycast(Point point)
        {
            double x = PianoGrid.TickAxis.Tick2X(Vibrato.GlobalEndPos());
            return point.X > x - 8 && point.X < x + 8;
        }
    }

    class VibratoFrequencyItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid), IVibratoItem
    {
        public Vibrato Vibrato { get; set; }

        public Point Position()
        {
            return ((IVibratoItem)this).FrequencyPosition();
        }

        public override bool Raycast(Point point)
        {
            var position = Position();
            if (double.IsNaN(position.Y))
                return false;

            return (point - position).ToVector().Length <= 12;
        }
    }

    class VibratoPhaseItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid), IVibratoItem
    {
        public Vibrato Vibrato { get; set; }

        public Point Position()
        {
            return ((IVibratoItem)this).PhasePosition();
        }

        public override bool Raycast(Point point)
        {
            var position = Position();
            if (double.IsNaN(position.Y))
                return false;

            return (point - position).ToVector().Length <= 12;
        }
    }

    class WaveformBackItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid)
    {
        public override bool Raycast(Point point)
        {
            return point.Y >= PianoGrid.WaveformTop && point.Y <= PianoGrid.WaveformBottom;
        }
    }

    class WaveformNoteResizeItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid)
    {
        public INote? Left;
        public INote? Right;

        public override bool Raycast(Point point)
        {
            if (Left == null && Right == null)
                return false;

            double center = (PianoGrid.WaveformTop + PianoGrid.WaveformBottom) / 2;
            double pos = Left == null ? Right!.GlobalStartPos() : Left.GlobalEndPos();
            double x = PianoGrid.TickAxis.Tick2X(pos);
            return point.Y >= center - 12 && point.Y <= center + 12 && point.X >= x - 8 && point.X <= x + 8;
        }
    }

    class WaveformPhonemeResizeItem(PianoGrid pianoGrid) : PianoGridItem(pianoGrid)
    {
        public INote Note;
        public int PhonemeIndex;

        public override bool Raycast(Point point)
        {
            IReadOnlyList<SynthesizedPhoneme>? phonemes = ((ISynthesisNote)Note).Phonemes;
            if (phonemes.IsEmpty())
                phonemes = Note.SynthesizedPhonemes;

            if (phonemes == null || phonemes.IsEmpty() || PhonemeIndex > phonemes.Count)
                return false;

            double center = (PianoGrid.WaveformTop + PianoGrid.WaveformBottom) / 2;
            double time = phonemes.Count == PhonemeIndex ? phonemes.ConstLast().EndTime : phonemes[PhonemeIndex].StartTime;
            double pos = Note.Part.TempoManager.GetTick(time);
            double x = PianoGrid.TickAxis.Tick2X(pos);
            return point.Y >= center - 12 && point.Y <= center + 12 && point.X >= x - 8 && point.X <= x + 8;
        }
    }
}
