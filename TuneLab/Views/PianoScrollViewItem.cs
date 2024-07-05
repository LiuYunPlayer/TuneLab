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
using Avalonia.Media;

namespace TuneLab.Views;

internal partial class PianoScrollView
{
    class PianoScrollViewItem(PianoScrollView pianoScrollView) : Item
    {
        public PianoScrollView PianoScrollView => pianoScrollView;
    }

    class NoteItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public required INote Note;

        public Rect Rect()
        {
            return PianoScrollView.NoteRect(Note);
        }

        public override bool Raycast(Avalonia.Point point)
        {
            return Rect().Contains(point);
        }
    }

    class NoteStartResizeItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public required INote Note;

        public override bool Raycast(Avalonia.Point point)
        {
            double x = PianoScrollView.TickAxis.Tick2X(Note.GlobalStartPos());
            double left = x - 8;
            double right = x + 8;
            var last = Note.Last;
            if (last != null && last.Pitch.Value == Note.Pitch.Value)
            {
                double lastX = PianoScrollView.TickAxis.Tick2X(last.GlobalEndPos());
                left = x - Math.Min(8, Math.Abs(lastX - x));
            }
            double top = PianoScrollView.PitchAxis.Pitch2Y(Note.Pitch.Value + 1);
            double bottom = PianoScrollView.PitchAxis.Pitch2Y(Note.Pitch.Value);
            return point.Y >= top && point.Y <= bottom && point.X >= left && point.X <= right;
        }
    }

    class NoteEndResizeItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public required INote Note;

        public override bool Raycast(Avalonia.Point point)
        {
            double x = PianoScrollView.TickAxis.Tick2X(Note.GlobalEndPos());
            double left = x - 8;
            double right = x + 8;
            double top = PianoScrollView.PitchAxis.Pitch2Y(Note.Pitch.Value + 1);
            double bottom = PianoScrollView.PitchAxis.Pitch2Y(Note.Pitch.Value);
            return point.Y >= top && point.Y <= bottom && point.X >= left && point.X <= right;
        }
    }

    class NotePronunciationItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public required INote Note;

        public override bool Raycast(Point point)
        {
            double left = PianoScrollView.TickAxis.Tick2X(Note.GlobalStartPos());
            double width = new FormattedText(Note.FinalPronunciation() ?? string.Empty, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, null).Width + 16;
            double bottom = PianoScrollView.PitchAxis.Pitch2Y(Note.Pitch.Value + 1);
            double top = bottom - 28;
            return new Rect(left, top, width, bottom - top).Contains(point);
        }
    }

    interface IVibratoItem
    {
        Vibrato Vibrato { get; }
        PianoScrollView PianoScrollView { get; }

        public Point FrequencyPosition()
        {
            double pitch = Pitch();
            double y = double.IsNaN(pitch) ? double.NaN : PianoScrollView.PitchAxis.Pitch2Y(pitch + Vibrato.Amplitude) - 24;
            return new Point(CenterX(), y);
        }

        public Point PhasePosition()
        {
            double pitch = Pitch();
            double y = double.IsNaN(pitch) ? double.NaN : PianoScrollView.PitchAxis.Pitch2Y(pitch - Vibrato.Amplitude) + 24;
            return new Point(CenterX(), y);
        }

        public Point AttackPosition()
        {
            double pitch = Pitch();
            double y = double.IsNaN(pitch) ? double.NaN : PianoScrollView.PitchAxis.Pitch2Y(pitch);
            return new Point(PianoScrollView.TickAxis.Tick2X(Vibrato.GlobalAttackTick()), y);
        }

        public Point ReleasePosition()
        {
            double pitch = Pitch();
            double y = double.IsNaN(pitch) ? double.NaN : PianoScrollView.PitchAxis.Pitch2Y(pitch);
            return new Point(PianoScrollView.TickAxis.Tick2X(Vibrato.GlobalReleaseTick()), y);
        }

        public double CenterX()
        {
            return PianoScrollView.TickAxis.Tick2X(Vibrato.GlobalStartPos() + Vibrato.Dur / 2);
        }

        public double Pitch()
        {
            return PianoScrollView.Part == null ? double.NaN : PianoScrollView.Part.Pitch.GetValue(Vibrato.Pos + Vibrato.Dur / 2) + 0.5;
        }
    }

    class VibratoItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView), IVibratoItem
    {
        public required Vibrato Vibrato { get; set; }

        public override bool Raycast(Point point)
        {
            double left = PianoScrollView.TickAxis.Tick2X(Vibrato.GlobalStartPos());
            double right = PianoScrollView.TickAxis.Tick2X(Vibrato.GlobalEndPos());
            return point.X > left && point.X < right;
        }
    }

    class VibratoAmplitudeItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView), IVibratoItem
    {
        public required Vibrato Vibrato { get; set; }

        public override bool Raycast(Point point)
        {
            double left = PianoScrollView.TickAxis.Tick2X(Vibrato.GlobalStartPos());
            double right = PianoScrollView.TickAxis.Tick2X(Vibrato.GlobalEndPos());
            double pitch = PianoScrollView.Part == null ? double.NaN : PianoScrollView.Part.Pitch.GetValue(Vibrato.Pos + Vibrato.Dur / 2);
            if (double.IsNaN(pitch))
                return false;

            double top = PianoScrollView.PitchAxis.Pitch2Y(pitch + 0.5 + Vibrato.Amplitude) - 24;
            double bottom = PianoScrollView.PitchAxis.Pitch2Y(pitch + 0.5 - Vibrato.Amplitude) + 24;
            return point.X > left && point.X < right && point.Y > top && point.Y < bottom;
        }
    }

    class VibratoStartResizeItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView), IVibratoItem
    {
        public required Vibrato Vibrato { get; set; }

        public override bool Raycast(Point point)
        {
            double x = PianoScrollView.TickAxis.Tick2X(Vibrato.GlobalStartPos());
            return point.X > x - 8 && point.X < x + 8;
        }
    }

    class VibratoEndResizeItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView), IVibratoItem
    {
        public required Vibrato Vibrato { get; set; }

        public override bool Raycast(Point point)
        {
            double x = PianoScrollView.TickAxis.Tick2X(Vibrato.GlobalEndPos());
            return point.X > x - 8 && point.X < x + 8;
        }
    }

    class VibratoFrequencyItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView), IVibratoItem
    {
        public required Vibrato Vibrato { get; set; }

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

    class VibratoPhaseItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView), IVibratoItem
    {
        public required Vibrato Vibrato { get; set; }

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

    class VibratoAttackItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView), IVibratoItem
    {
        public required Vibrato Vibrato { get; set; }

        public Point Position()
        {
            return ((IVibratoItem)this).AttackPosition();
        }

        public override bool Raycast(Point point)
        {
            var position = Position();
            if (double.IsNaN(position.Y))
                return false;

            return new Rect(position, position).Adjusted(-8, -8, 8, 8).Contains(point);
        }
    }

    class VibratoReleaseItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView), IVibratoItem
    {
        public required Vibrato Vibrato { get; set; }

        public Point Position()
        {
            return ((IVibratoItem)this).ReleasePosition();
        }

        public override bool Raycast(Point point)
        {
            var position = Position();
            if (double.IsNaN(position.Y))
                return false;

            return new Rect(position, position).Adjusted(-8, -8, 8, 8).Contains(point);
        }
    }

    class AnchorItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public required AnchorPoint AnchorPoint { get; set; }

        public Point Position()
        {
            if (PianoScrollView.Part == null)
                return new Point();

            return new Point(PianoScrollView.TickAxis.Tick2X(PianoScrollView.Part.Pos + AnchorPoint.Pos), PianoScrollView.PitchAxis.Pitch2Y(AnchorPoint.Value + 0.5));
        }

        public override bool Raycast(Point point)
        {
            return Point.Distance(Position(), point) <= 6;
        }

        public override void Render(DrawingContext context)
        {
            AnchorPoint? hoverAnchor = (PianoScrollView.HoverItem() as AnchorItem)?.AnchorPoint;
            var center = Position();
            context.DrawEllipse(PointBrush, null, center, 2, 2);
            if (AnchorPoint.IsSelected)
            {
                context.DrawEllipse(null, SelectedPointPen, center, 5.5, 5.5);
            }
            else if (AnchorPoint == hoverAnchor)
            {
                context.DrawEllipse(null, HoverPointPen, center, 5.5, 5.5);
            }
        }

        static IBrush PointBrush = Color.Parse(ConstantDefine.PitchColor).ToBrush();
        static IPen SelectedPointPen = new Pen(PointBrush);
        static IPen HoverPointPen = new Pen(Brushes.White);
    }

    class PreviewAnchorGroupItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public required IPiecewiseCurve PiecewiseCurve { get; set; }

        public override void Render(DrawingContext context)
        {
            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.DrawPitch(context, 0, PianoScrollView.Bounds.Width, PiecewiseCurve.GetValues, Colors.White, 1);
        }
    }

    class WaveformBackItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public override bool Raycast(Point point)
        {
            return point.Y >= PianoScrollView.WaveformTop && point.Y <= PianoScrollView.WaveformBottom;
        }
    }

    class WaveformNoteResizeItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public INote? Left;
        public INote? Right;

        public override bool Raycast(Point point)
        {
            if (Left == null && Right == null)
                return false;

            double center = (PianoScrollView.WaveformTop + PianoScrollView.WaveformBottom) / 2;
            double pos = Left == null ? Right!.GlobalStartPos() : Left.GlobalEndPos();
            double x = PianoScrollView.TickAxis.Tick2X(pos);
            return point.Y >= center - 12 && point.Y <= center + 12 && point.X >= x - 8 && point.X <= x + 8;
        }
    }

    class WaveformPhonemeResizeItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public required INote Note;
        public int PhonemeIndex;

        public override bool Raycast(Point point)
        {
            IReadOnlyList<SynthesizedPhoneme>? phonemes = ((ISynthesisNote)Note).Phonemes;
            if (phonemes.IsEmpty())
                phonemes = Note.SynthesizedPhonemes;

            if (phonemes == null || phonemes.IsEmpty() || PhonemeIndex > phonemes.Count)
                return false;

            double center = (PianoScrollView.WaveformTop + PianoScrollView.WaveformBottom) / 2;
            double time = phonemes.Count == PhonemeIndex ? phonemes.ConstLast().EndTime : phonemes[PhonemeIndex].StartTime;
            double pos = Note.Part.TempoManager.GetTick(time);
            double x = PianoScrollView.TickAxis.Tick2X(pos);
            return point.Y >= center - 12 && point.Y <= center + 12 && point.X >= x - 8 && point.X <= x + 8;
        }
    }
}
