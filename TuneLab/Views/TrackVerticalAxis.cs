using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.GUI;
using TuneLab.Data;
using TuneLab.Utils;
using TuneLab.Base.Utils;

namespace TuneLab.Views;

internal class TrackVerticalAxis : AnimationScalableScrollAxis
{
    public double TrackHeight
    {
        get => Factor - 1;
        set => Factor = value + 1;
    }

    public readonly struct Position
    {
        public readonly double Y => mAxis.Pos2Coor(mFlag);
        public readonly int TrackIndex => (int)mFlag;
        public Position(TrackVerticalAxis axis, double y)
        {
            mAxis = axis;
            mFlag = axis.Coor2Pos(y);
        }

        readonly TrackVerticalAxis mAxis;
        readonly double mFlag;
    }

    public interface IDependency
    {
        IProvider<Project> ProjectProvider { get; }
    }

    public TrackVerticalAxis(IDependency dependency)
    {
        mDependency = dependency;

        TrackHeight = 64;

        mDependency.ProjectProvider.When(p => p.Tracks.Modified).Subscribe(OnTracksChanged, s);
        mDependency.ProjectProvider.ObjectChanged.Subscribe(OnProjectChanged, s);

        OnProjectChanged();
    }

    ~TrackVerticalAxis()
    {
        s.DisposeAll();
    }

    public double GetTop(int index)
    {
        return Pos2Coor(index);
    }

    public double GetBottom(int index)
    {
        return GetTop(index) + TrackHeight;
    }

    public Position GetPosition(double y)
    {
        return new(this, y);
    }

    public void SetAutoContentSize(bool isAuto)
    {
        mIsAutoContentSize = isAuto;
        if (isAuto)
        {
            OnTracksChanged();
        }
        else
        {
            ContentSize = int.MaxValue;
        }
    }

    void OnProjectChanged()
    {
        OnTracksChanged();
    }

    void OnTracksChanged()
    {
        if (!mIsAutoContentSize)
            return;

        var project = mDependency.ProjectProvider.Object;
        if (project == null)
            return;

        ContentSize = project.Tracks.Count + 1;
    }

    bool mIsAutoContentSize = true;

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
