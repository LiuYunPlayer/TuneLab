using Avalonia;
using Avalonia.Media;
using TuneLab.GUI;
using TuneLab.Utils;

namespace TuneLab.UI;

internal partial class PianoRoll
{
    class PianoRollItem(PianoRoll pianoRoll) : Item
    {
        public PianoRoll PianoRoll => pianoRoll;
    }

    interface IKeyItem
    {
        int KeyNumber { get; }
    }

    class WhiteKeyItem(PianoRoll pianoRoll) : PianoRollItem(pianoRoll), IKeyItem
    {
        public required Rect Rect { get; set; }
        public required int KeyNumber { get; set; }

        public override void Render(DrawingContext context)
        {
            var whiteKeyBrush = PianoRoll.HoverItem() == this ? HighLightKeyBrush : WhiteKeyBrush;
            context.FillRectangle(whiteKeyBrush, Rect, 4);
        }

        public override bool Raycast(Point point)
        {
            return Rect.Contains(point);
        }

        static readonly IBrush WhiteKeyBrush = new Color(255, 204, 204, 204).ToBrush();
    }

    class BlackKeyItem(PianoRoll pianoRoll) : PianoRollItem(pianoRoll), IKeyItem
    {
        public required Rect Rect { get; set; }
        public required int KeyNumber { get; set; }

        public override void Render(DrawingContext context)
        {
            var blackKeyBrush = PianoRoll.HoverItem() == this ? HighLightKeyBrush : BlackKeyBrush;
            context.FillRectangle(blackKeyBrush, Rect);
        }

        public override bool Raycast(Point point)
        {
            return Rect.Contains(point);
        }

        static readonly IBrush BlackKeyBrush = Style.BACK.ToBrush();
    }

    class TextItem(PianoRoll pianoRoll) : PianoRollItem(pianoRoll)
    {
        public required string Text { get; set; }

        public double Bottom { get; set; }

        public override void Render(DrawingContext context)
        {
            context.DrawString(Text, new Rect(0, Bottom - 24, PianoRoll.Bounds.Width, 24), textBrush, 12, Alignment.RightCenter, Alignment.RightCenter, new(-6, 0));
        }

        static readonly IBrush textBrush = Brushes.Black;
    }

    static IBrush HighLightKeyBrush = Style.DefaultTrackColor.ToBrush();
}
