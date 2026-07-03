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
using TuneLab.SDK;
using TuneLab.Foundation;
using Avalonia.Media;
using TuneLab.GUI.Input;

using Point = Avalonia.Point;

namespace TuneLab.UI;

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
            double left = x - 4;
            double right = x + 4;
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
            double left = x - 4;
            double right = x + 4;
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
            return PianoScrollView.Part == null ? double.NaN : PianoScrollView.Part.GetEffectivePitchValue(Vibrato.Pos + Vibrato.Dur / 2) + 0.5;
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
            double pitch = PianoScrollView.Part == null ? double.NaN : PianoScrollView.Part.GetEffectivePitchValue(Vibrato.Pos + Vibrato.Dur / 2);
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
            return point.X > x - 4 && point.X < x + 4;
        }
    }

    class VibratoEndResizeItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView), IVibratoItem
    {
        public required Vibrato Vibrato { get; set; }

        public override bool Raycast(Point point)
        {
            double x = PianoScrollView.TickAxis.Tick2X(Vibrato.GlobalEndPos());
            return point.X > x - 4 && point.X < x + 4;
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

            return new Point(PianoScrollView.TickAxis.Tick2X(PianoScrollView.Part.Pos.Value + AnchorPoint.Pos), PianoScrollView.PitchAxis.Pitch2Y(AnchorPoint.Value + 0.5));
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

        static IBrush PointBrush = Brushes.White;
        static IPen SelectedPointPen = new Pen(PointBrush);
        static IPen HoverPointPen = new Pen(Brushes.White);
    }

    class PreviewAnchorGroupItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public required IPiecewiseAutomation PiecewiseAutomation { get; set; }
        public Action<MouseDownEventArgs, bool>? OnDown { get; set; } = null;

        public override void Render(DrawingContext context)
        {
            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.DrawPitch(context, 0, PianoScrollView.Bounds.Width, PiecewiseAutomation.GetValues, Colors.White, 1);
        }
    }

    class WaveformBackItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public override bool Raycast(Point point)
        {
            return point.Y >= PianoScrollView.WaveformTop && point.Y <= PianoScrollView.WaveformBottom;
        }
    }

    // 波形带上下分层（仅 voice 有这些热区）：上半区 = note 边界操作、下半区 = 音素边界操作。
    // 音素柄只占下半区；note 边界热区全带高（贯穿线语义，见下），两者在下半区的重叠由 items 顺序消歧（note 后加优先）。
    class WaveformPhonemeResizeItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public required INote Note;
        public int PhonemeIndex;

        public override bool Raycast(Point point)
        {
            var phonemes = Note.DisplayPhonemes;
            if (phonemes.IsEmpty() || PhonemeIndex > phonemes.Count)
                return false;

            double time = phonemes.Count == PhonemeIndex ? phonemes.ConstLast().EndTime : phonemes[PhonemeIndex].StartTime;
            double pos = Note.Part.TempoManager.GetTick(time);
            double x = PianoScrollView.TickAxis.Tick2X(pos);
            return point.Y >= PianoScrollView.WaveformCenterY && point.Y <= PianoScrollView.WaveformBottom && point.X >= x - 8 && point.X <= x + 8;
        }
    }

    // note 头/尾缩放热区：note 边界贯穿上下半区（上半杆 + 下半的核起点线/末音素终点线是同一条边界的上下两段），
    // 沿线任意高度都可拖；后加优先，线 ±8 内盖过下半区的音素柄/双击编辑。**下探与下半段线的存在性对齐**——
    // 该 note 无显示音素（延音符乘客被铺过 / 未合成）时下半区没画对应线，热区也只占上半区，不抢音素域
    // （典型：melisma 中延音符头的分界线下方正是前 note 铺过来的元音区间，音素操作必须保留）。
    class WaveformNoteStartResizeItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public required INote Note;

        public override bool Raycast(Point point)
        {
            double x = PianoScrollView.TickAxis.Tick2X(Note.GlobalStartPos());
            double bottom = Note.DisplayPhonemes.IsEmpty() ? PianoScrollView.WaveformCenterY : PianoScrollView.WaveformBottom;
            return point.Y >= PianoScrollView.WaveformTop && point.Y <= bottom && point.X >= x - 8 && point.X <= x + 8;
        }
    }

    class WaveformNoteEndResizeItem(PianoScrollView pianoScrollView) : PianoScrollViewItem(pianoScrollView)
    {
        public required INote Note;

        public override bool Raycast(Point point)
        {
            double x = PianoScrollView.TickAxis.Tick2X(Note.GlobalEndPos());
            double bottom = ReachesPhonemeLane() ? PianoScrollView.WaveformBottom : PianoScrollView.WaveformCenterY;
            return point.Y >= PianoScrollView.WaveformTop && point.Y <= bottom && point.X >= x - 8 && point.X <= x + 8;
        }

        // 尾杆的下探判定与头杆不同：自己有显示音素之外，作为被铺乘客（display 空）时若沿相接链回溯存在
        // 有显示音素的 note，其元音正铺到本 note 尾（FillEnd 对齐有效末）——下半区在此画的正是那条末线
        //（见 DrawWaveform 的 endOwner），故也全带高可拖。
        bool ReachesPhonemeLane()
        {
            if (!Note.DisplayPhonemes.IsEmpty())
                return true;

            var cur = Note;
            while (true)
            {
                var prev = cur.Last;
                if (prev == null || prev.EndPos() < cur.StartPos() - 1e-6)
                    return false;

                if (!prev.DisplayPhonemes.IsEmpty())
                    return true;

                cur = prev;
            }
        }
    }
}
