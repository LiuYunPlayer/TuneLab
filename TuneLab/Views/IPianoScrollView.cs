using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using TuneLab.Data;

namespace TuneLab.Views;

internal interface IPianoScrollView
{
    TickAxis TickAxis { get; }
    PitchAxis PitchAxis { get; }
}

internal static class IPianoScrollViewExtension
{
    public static Rect NoteRect(this IPianoScrollView view, INote note)
    {
        double x = view.TickAxis.Tick2X(note.GlobalStartPos());
        double y = view.PitchAxis.Pitch2Y(note.Pitch.Value + 1);
        double w = note.Dur.Value * view.TickAxis.PixelsPerTick;
        double h = view.PitchAxis.KeyHeight;
        return new Rect(x, y, w, h);
    }
    public static Rect GuideRect(this IPianoScrollView view, INote note)
    {
        double keyHeight = view.PitchAxis.KeyHeight / 10.0d;
        double keyOffset = (view.PitchAxis.KeyHeight - keyHeight) / 2;
        double x = view.TickAxis.Tick2X(note.GlobalStartPos());
        double y = view.PitchAxis.Pitch2Y(note.Pitch.Value + 1) +  keyOffset;
        double w = note.Dur.Value * view.TickAxis.PixelsPerTick;
        double h = keyHeight;
        return new Rect(x, y, w, h);
    }
}