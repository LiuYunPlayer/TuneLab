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

        Children.Add(new BackLayer(this));

        mTrackHeadLayer = new(this);
        Children.Add(mTrackHeadLayer);
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

            mTrackHeadList.mDependency.TrackVerticalAxis.AxisChanged += InvalidateArrange;
            mTrackHeadList.mDependency.ProjectProvider.ObjectChanged.Subscribe(OnTrackListModified, s);
            mTrackHeadList.mDependency.ProjectProvider.When(project => project.Tracks.ListModified).Subscribe(OnTrackListModified, s);

            ClipToBounds = true;
        }

        ~TrackHeadLayer()
        {
            s.DisposeAll();
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

        void OnTrackListModified()
        {
            foreach (var child in Children)
            {
                if (child is TrackHead trackHead)
                {
                    trackHead.SetTrack(null);
                    ObjectPoolManager.Return(trackHead);
                }
            }
            Children.Clear();

            var project = mTrackHeadList.mDependency.ProjectProvider.Object;
            if (project == null)
                return;

            foreach (var track in project.Tracks)
            {
                var trackHead = ObjectPoolManager.Get<TrackHead>();
                trackHead.SetTrack(track);
                Children.Add(trackHead);
            }
            Children.Add(mAddTrackButton);
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
        readonly DisposableManager s = new();
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

    readonly TrackHeadLayer mTrackHeadLayer;
    readonly IDependency mDependency;
}
