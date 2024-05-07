using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.Utils;
using TuneLab.Base.Science;

namespace TuneLab.Views;

internal partial class PianoRoll : View
{
    public interface IDependency
    {
        PitchAxis PitchAxis { get; }
    }

    public PianoRoll(IDependency dependency)
    {
        mDependency = dependency;

        mMiddleDragOperation = new(this);

        PitchAxis.AxisChanged += InvalidateVisual;
    }

    ~PianoRoll()
    {
        PitchAxis.AxisChanged -= InvalidateVisual;
    }

    protected override void OnRender(DrawingContext context)
    {
        IBrush blackKeyBrush = BlackKey.ToBrush();
        context.FillRectangle(blackKeyBrush, this.Rect());

        IBrush whiteKeyBrush = WhiteKey.ToBrush();
        double hideHeight = PitchAxis.LargeEndHideLength;
        double keyHeight = PitchAxis.KeyHeight;
        double groupHeight = keyHeight * 12;
        double whiteKeyHeight = groupHeight / 7;
        double c0hide = hideHeight - (MusicTheory.C0_PITCH - MusicTheory.MIN_PITCH) * keyHeight;
        int minWhite = (int)Math.Floor(c0hide / whiteKeyHeight);
        int maxWhite = (int)Math.Ceiling((c0hide + Bounds.Height) / whiteKeyHeight);
        for (int i = minWhite; i < maxWhite; i++)
        {
            double bottom = PitchAxis.Pitch2Y(MusicTheory.C0_PITCH + (double)i * 12 / 7) - 0.5;
            double top = PitchAxis.Pitch2Y(MusicTheory.C0_PITCH + (double)(i + 1) * 12 / 7) + 0.5;
            context.FillRectangle(whiteKeyBrush, new Rect(-4, top, Bounds.Width + 4, bottom - top), 4);
        }

        int minBlack = (int)Math.Floor(PitchAxis.MinVisiblePitch);
        int maxBlack = (int)Math.Ceiling(PitchAxis.MaxVisiblePitch);
        for (int i = minBlack; i < maxBlack; i++)
        {
            if (MusicTheory.IsBlack(i))
            {
                double top = PitchAxis.Pitch2Y(i + 1);
                context.FillRectangle(blackKeyBrush, new Rect(0, top, 32, keyHeight));
            }
        }

        IBrush textBrush = Colors.Black.ToBrush();
        int minText = (int)Math.Floor(c0hide / groupHeight);
        int maxText = (int)Math.Ceiling((c0hide + Bounds.Height) / groupHeight);
        for (int i = minText; i < maxText; i++)
        {
            double bottom = PitchAxis.Pitch2Y(MusicTheory.C0_PITCH + i * 12);
            var formattedText = new FormattedText("C" + i, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, FontSize, textBrush);
            context.DrawText(formattedText, new Point(Bounds.Width - 6 - formattedText.Width, bottom - 12 - formattedText.Height / 2));
        }
    }

    PitchAxis PitchAxis => mDependency.PitchAxis;

    double FontSize => 12;
    Color WhiteKey => new(255, 204, 204, 204);
    Color BlackKey => GUI.Style.BACK;

    readonly IDependency mDependency;
}
