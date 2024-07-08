using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Base.Utils;
using TuneLab.Utils;
using Avalonia.Threading;

namespace TuneLab.Views;

internal class TrackHeadList : LayerPanel
{
    public interface IDependency
    {
        IProvider<IProject> ProjectProvider { get; }
        TrackVerticalAxis TrackVerticalAxis { get; }
    }

    public TrackHeadList(IDependency dependency)
    {
        mDependency = dependency;

        mBackLayer = new BackLayer(this);
        Children.Add(mBackLayer);

        mTrackHeadLayer = new(this);
        Children.Add(mTrackHeadLayer);

        mDirtyHandler.OnDirty += () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                mTrackHeadLayer.OnTrackListModified();
                mBackLayer.InvalidateVisual();
                mDirtyHandler.Reset();
            }, DispatcherPriority.Normal);
        };

        mDependency.TrackVerticalAxis.AxisChanged += mTrackHeadLayer.InvalidateArrange;
        mDependency.ProjectProvider.ObjectChanged.Subscribe(mDirtyHandler.SetDirty, s);
        mDependency.ProjectProvider.When(project => project.Tracks.ListModified).Subscribe(mDirtyHandler.SetDirty, s);
    }

    ~TrackHeadList()
    {
        s.DisposeAll();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var deltaY = e.Delta.Y;
        if (deltaY != 0) mDependency.TrackVerticalAxis.AnimateMove(deltaY * 70);
    }

    class TrackHeadLayer : Panel
    {
        public TrackHeadLayer(TrackHeadList trackHeadList)
        {
            mTrackHeadList = trackHeadList;

            mAddTrackButton = new(mTrackHeadList);
            

            ClipToBounds = true;
            Children.Add(mAddTrackButton);
        }

        ~TrackHeadLayer()
        {

        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double offset = mTrackHeadList.mDependency.TrackVerticalAxis.ViewOffset;
            double height = mTrackHeadList.mDependency.TrackVerticalAxis.Factor;
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].Arrange(new(0, i * height - offset, finalSize.Width, height));
            }
            return finalSize;
        }

        public void OnTrackListModified()
        {
            int trackCount = 0;
            foreach (var child in Children)
            {
                if (child is TrackHead trackHead)
                {
                    trackHead.SetTrack(null);
                    trackCount++;
                }
            }

            var project = mTrackHeadList.mDependency.ProjectProvider.Object;
            if (project == null)
                return;

            int diffCount = project.Tracks.Count - trackCount;
            if (diffCount > 0)
            {
                for (int i = 0; i < diffCount; i++)
                {
                    var trackHead = ObjectPoolManager.Get<TrackHead>();
                    Children.Insert(Children.Count - 1, trackHead);
                }
            }
            if (diffCount < 0)
            {
                for (int i = 0; i < -diffCount; i++)
                {
                    var trackHead = (TrackHead)Children[Children.Count - 2];
                    Children.RemoveAt(Children.Count - 2);
                    ObjectPoolManager.Return(trackHead);
                }
            }

            int childrenIndex = 0;
            foreach (var track in project.Tracks)
            {
                var trackHead = (TrackHead)Children[childrenIndex++];
                trackHead.SetTrack(track, childrenIndex);
            }
        }

        class AddTrackButton(TrackHeadList trackHeadList) : Component
        {
            protected override void OnMouseUp(MouseUpEventArgs e)
            {
                if (e.MouseButtonType != MouseButtonType.PrimaryButton)
                    return;

                var project = trackHeadList.mDependency.ProjectProvider.Object;
                if (project == null)
                    return;

                project.NewTrack();
                project.Commit();
            }

            public override void Render(DrawingContext context)
            {
                context.FillRectangle(Brushes.Transparent, this.Rect());
                var center = this.Rect().Center;
                var centerRect = new Rect(center, new Size());
                context.FillRectangle(Style.LIGHT_WHITE.ToBrush(), centerRect.Adjusted(-1, -16, 1, 16));
                context.FillRectangle(Style.LIGHT_WHITE.ToBrush(), centerRect.Adjusted(-16, -1, 16, 1));
            }
        }

        readonly AddTrackButton mAddTrackButton;
        readonly TrackHeadList mTrackHeadList;
    }

    class BackLayer : Component
    {
        public BackLayer(TrackHeadList trackHeadList)
        {
            mTrackHeadList = trackHeadList;

            mTrackHeadList.mDependency.TrackVerticalAxis.AxisChanged += InvalidateVisual;
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Style.INTERFACE.ToBrush(), this.Rect());
            var project = mTrackHeadList.mDependency.ProjectProvider.Object;
            if (project == null)
                return;

            for (int i = 0; i < project.Tracks.Count; i++)
            {
                double lineBottom = TrackVerticalAxis.GetTop(i + 1);
                if (lineBottom <= 0)
                    continue;

                double top = TrackVerticalAxis.GetTop(i);
                if (top >= Bounds.Height)
                    break;

                context.FillRectangle(Style.BACK.ToBrush(), new(0, lineBottom - 1, Bounds.Width, 1));
            }
        }

        TrackVerticalAxis TrackVerticalAxis => mTrackHeadList.mDependency.TrackVerticalAxis;

        readonly TrackHeadList mTrackHeadList;
    }

    readonly DirtyHandler mDirtyHandler = new();
    readonly DisposableManager s = new();

    readonly BackLayer mBackLayer;
    readonly TrackHeadLayer mTrackHeadLayer;
    readonly IDependency mDependency;
}
