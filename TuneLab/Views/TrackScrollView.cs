using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Data;
using TuneLab.GUI.Components;
using TuneLab.Base.Utils;
using TuneLab.GUI;
using TuneLab.Base.Science;

namespace TuneLab.Views;

internal class TrackScrollView : Panel, TrackGrid.IDependency
{
    public TickAxis TickAxis => mDependency.TickAxis;
    public TrackVerticalAxis TrackVerticalAxis => mDependency.TrackVerticalAxis;
    public IQuantization Quantization => mDependency.Quantization;
    public IProvider<Project> ProjectProvider => mDependency.ProjectProvider;
    public IProvider<Part> EditingPart => mDependency.EditingPart;
    public void SwitchEditingPart(IPart part) => mDependency.SwitchEditingPart(part);
    public TrackGrid TrackGrid => mTrackGrid;

    public interface IDependency
    {
        TickAxis TickAxis { get; }
        TrackVerticalAxis TrackVerticalAxis { get; }
        IQuantization Quantization { get; }
        IProvider<Project> ProjectProvider { get; }
        IProvider<Part> EditingPart { get; }
        void SwitchEditingPart(IPart part);
    }

    public TrackScrollView(IDependency dependency)
    {
        mDependency = dependency;

        mTrackGrid = new TrackGrid(this);
        Children.Add(mTrackGrid);

        mNameInput = new TextInput()
        {
            Padding = new(8, 0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            CornerRadius = new(4),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderThickness = new(0),
            FontSize = NameInputFontSize,
            CaretBrush = Brushes.Black,
            IsVisible = false
        };
        mNameInput.EndInput.Subscribe(OnNameInputComplete);
        Children.Add(mNameInput);

        ClipToBounds = true;

        TickAxis.AxisChanged += InvalidateArrange;
        TrackVerticalAxis.AxisChanged += InvalidateArrange;
    }

    ~TrackScrollView()
    {
        TickAxis.AxisChanged -= InvalidateArrange;
        TrackVerticalAxis.AxisChanged -= InvalidateArrange;
    }

    public void EnterInputPartName(IPart part, int trackIndex)
    {
        if (mInputNamePart != null)
            return;

        mInputNamePart = part;
        mInputNameTrackIndex = trackIndex;
        mNameInput.Text = part.Name.Value;
        mNameInput.IsVisible = true;
        mNameInput.Focus();
        mNameInput.SelectAll();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        mTrackGrid.Arrange(new Rect(finalSize));
        if (mNameInput.IsVisible)
            mNameInput.Arrange(NameInputRect());

        return finalSize;
    }

    void OnNameInputComplete()
    {
        if (mInputNamePart == null)
            return;

        var newLyric = mNameInput.Text;
        if (!string.IsNullOrEmpty(newLyric) && newLyric != mInputNamePart.Name.Value)
        {
            mInputNamePart.Name.Set(newLyric);
            mInputNamePart.Commit();
        }

        mNameInput.IsVisible = false;
        mInputNamePart = null;
    }

    Rect NameInputRect()
    {
        if (mInputNamePart == null)
            return new Rect();

        double x = Math.Max(0, TickAxis.Tick2X(mInputNamePart.StartPos()));
        double y = TrackVerticalAxis.GetTop(mInputNameTrackIndex);
        double w = TickAxis.Tick2X(mInputNamePart.EndPos()) - x;
        double h = NameInputHeight;
        return new Rect(x, y, w.Limit(NameInputMinWidth, NameInputMaxWidth), h);
    }

    IPart? mInputNamePart = null;
    int mInputNameTrackIndex;

    const int NameInputFontSize = 12;
    const double NameInputHeight = 16;
    const double NameInputMinWidth = 60;
    const double NameInputMaxWidth = 600;

    readonly IDependency mDependency;

    readonly TrackGrid mTrackGrid;
    readonly TextInput mNameInput;
}