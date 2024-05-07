using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;
using Avalonia.Input;
using System;
using TuneLab.GUI;
using TuneLab.Data;
using TuneLab.Base.Event;
using Avalonia.Styling;
using TuneLab.Base.Data;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.Views;

internal class PianoScrollView : Panel, IPianoScrollView, PianoGrid.IDependency
{
    public IActionEvent PianoToolChanged => mDependency.PianoToolChanged;
    public TickAxis TickAxis => mDependency.TickAxis;
    public PitchAxis PitchAxis => mDependency.PitchAxis;
    public IQuantization Quantization => mDependency.Quantization;
    public ParameterButton PitchButton => mDependency.PitchButton;
    public MidiPart? Part => mDependency.PartProvider.Object;
    public PianoGrid PianoGrid => mPianoGrid;
    public PianoTool PianoTool => mDependency.PianoTool;
    public IPlayhead Playhead => mDependency.Playhead;
    public double WaveformBottom => mDependency.WaveformBottom;
    public IActionEvent WaveformBottomChanged => mDependency.WaveformBottomChanged;
    public interface IDependency
    {
        IActionEvent PianoToolChanged { get; }
        TickAxis TickAxis { get; }
        PitchAxis PitchAxis { get; }
        IQuantization Quantization { get; }
        ParameterButton PitchButton { get; }
        IProvider<MidiPart> PartProvider { get; }
        PianoTool PianoTool { get; }
        IPlayhead Playhead { get; }
        double WaveformBottom { get; }
        IActionEvent WaveformBottomChanged { get; }
    }

    public PianoScrollView(IDependency dependency)
    {
        mDependency = dependency;

        mPianoGrid = new PianoGrid(this);
        Children.Add(mPianoGrid);

        mLyricInput = new TextInput()
        {
            Padding = new Thickness(8, LyricInputVerticalPadding),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderThickness = new(0),
            FontSize = LyricInputFontSize,
            CaretBrush = Brushes.Black,
            IsVisible = false
        };
        mLyricInput.EndInput.Subscribe(OnLyricInputComplete);
        Children.Add(mLyricInput);

        ClipToBounds = true;

        TickAxis.AxisChanged += InvalidateArrange;
        PitchAxis.AxisChanged += InvalidateArrange;
    }

    ~PianoScrollView()
    {
        TickAxis.AxisChanged -= InvalidateArrange;
        PitchAxis.AxisChanged -= InvalidateArrange;
    }

    public void EnterInputLyric(INote note)
    {
        if (mInputLyricNote != null)
            return;

        mInputLyricNote = note;
        mLyricInput.Text = note.Lyric.Value;
        mLyricInput.IsVisible = true;
        mLyricInput.Focus();
        mLyricInput.SelectAll();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        mPianoGrid.Arrange(new Rect(finalSize));
        if (mLyricInput.IsVisible)
            mLyricInput.Arrange(LyricInputRect());

        return finalSize;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Match(Key.Tab))
        {
            if (Part != null && mInputLyricNote != null)
            {
                var x = TickAxis.Tick2X(mInputLyricNote.GlobalStartPos());
                var y = PitchAxis.Pitch2Y(mInputLyricNote.Pitch.Value);
                var nextNote = mInputLyricNote.Next;
                mLyricInput.Unfocus();
                if (nextNote != null)
                {
                    EnterInputLyric(nextNote);
                    TickAxis.AnimateMove(x - TickAxis.Tick2X(nextNote.GlobalStartPos()));
                    PitchAxis.AnimateMove(y - PitchAxis.Pitch2Y(nextNote.Pitch.Value));
                }
                e.Handled = true;
            }
        }

        if (e.IsHandledByTextBox())
            return;
    }

    void OnLyricInputComplete()
    {
        if (mInputLyricNote == null)
            return;

        var newLyric = mLyricInput.Text;
        if (!string.IsNullOrEmpty(newLyric) && newLyric != mInputLyricNote.Lyric.Value)
        {
            mInputLyricNote.Lyric.Set(newLyric);
            mInputLyricNote.Commit();
        }

        mLyricInput.IsVisible = false;
        mInputLyricNote = null;
    }

    Rect LyricInputRect()
    {
        if (mInputLyricNote == null)
            return new Rect();

        double x = TickAxis.Tick2X(mInputLyricNote.GlobalStartPos());
        double y = PitchAxis.Pitch2Y(mInputLyricNote.Pitch.Value + 0.5);
        double w = mInputLyricNote.Dur.Value * TickAxis.PixelsPerTick;
        double h = LyricInputHeight;
        return new Rect(x, y - h / 2, Math.Max(w, LyricInputMinWidth), h);
    }

    INote? mInputLyricNote = null;

    const int LyricInputFontSize = 12;
    const double LyricInputVerticalPadding = 8;
    const double LyricInputHeight = LyricInputFontSize + 2 * LyricInputVerticalPadding;
    const double LyricInputMinWidth = 60;

    IProvider<MidiPart> PianoGrid.IDependency.PartProvider => mDependency.PartProvider;

    readonly IDependency mDependency;

    readonly PianoGrid mPianoGrid;
    readonly TextInput mLyricInput;
}
