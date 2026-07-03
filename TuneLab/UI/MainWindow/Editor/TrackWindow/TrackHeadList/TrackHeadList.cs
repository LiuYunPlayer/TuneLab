using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Utils;
using Avalonia.Threading;

namespace TuneLab.UI;

internal class TrackHeadList : LayerPanel
{
    public interface IDependency
    {
        IHolder<IProject> ProjectHolder { get; }
        TrackVerticalAxis TrackVerticalAxis { get; }
    }

    public TrackHeadList(IDependency dependency)
    {
        mDependency = dependency;

        mBackLayer = new BackLayer(this);
        Children.Add(mBackLayer);

        mTrackHeadLayer = new(this);
        Children.Add(mTrackHeadLayer);

        // 轨道列表变化合拍重建（单级）：一拍内的多次数据变更并成一次刷新。
        mTrackListRefresh = new(() =>
        {
            mTrackHeadLayer.OnTrackListModified();
            mBackLayer.InvalidateVisual();
        });

        mDependency.TrackVerticalAxis.AxisChanged += mTrackHeadLayer.InvalidateArrange;
        mDependency.ProjectHolder.Modified.Subscribe(mTrackListRefresh.InvalidateStructure, s);
        mDependency.ProjectHolder.When(project => project.Tracks.MembershipModified).Subscribe(mTrackListRefresh.InvalidateStructure, s);
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

    class TrackHeadLayer : Panel, ITrackHeadDragHost
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

        // 竖向定位统一交给 TrackVerticalAxis（drag-aware）：子索引即轨道索引，新建按钮在末尾槽。
        // 拖拽中的让位/跟手/缓动全在 axis.GetVisualTop 内，本层只负责把结果摆上去。
        protected override Size ArrangeOverride(Size finalSize)
        {
            var axis = mTrackHeadList.mDependency.TrackVerticalAxis;
            double height = axis.Factor;
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].Arrange(new(0, axis.GetVisualTop(i), finalSize.Width, height));
            }
            return finalSize;
        }

        public void BeginTrackHeadDrag(TrackHead head, double grabOffsetY)
        {
            var project = mTrackHeadList.mDependency.ProjectHolder.Value;
            if (project == null)
                return;

            int primary = Children.IndexOf(head);
            if (primary < 0)
                return;

            // 被拖的是整组选中轨（抓住的那条已由 SelectOnPress 保证在选区内）；若抓的轨未选中则只拖它。
            var indices = new List<int>();
            for (int i = 0; i < project.Tracks.Count; i++)
                if (project.Tracks[i].IsSelected)
                    indices.Add(i);
            if (!indices.Contains(primary))
            {
                indices.Clear();
                indices.Add(primary);
            }

            mDraggedHeads.Clear();
            foreach (int idx in indices)
                if (idx < Children.Count && Children[idx] is TrackHead h)
                {
                    h.ZIndex = 1;   // 整组浮在其余轨道头之上
                    mDraggedHeads.Add(h);
                }

            mTrackHeadList.mDependency.TrackVerticalAxis.BeginTrackDrag(indices, primary, grabOffsetY);
        }

        public void UpdateTrackHeadDrag(double pointerYInHost)
        {
            if (mDraggedHeads.Count == 0)
                return;

            mTrackHeadList.mDependency.TrackVerticalAxis.UpdateTrackDrag(pointerYInHost);
        }

        public void EndTrackHeadDrag()
        {
            if (mDraggedHeads.Count == 0)
                return;

            var axis = mTrackHeadList.mDependency.TrackVerticalAxis;
            int delta = axis.DragDelta;
            foreach (var h in mDraggedHeads)
                h.ZIndex = 0;

            // mDraggedHeads 按升序索引添加，故 tracks 即原始相对次序的选中块。
            var tracks = new List<ITrack>();
            foreach (var h in mDraggedHeads)
                if (h.Track != null)
                    tracks.Add(h.Track);
            mDraggedHeads.Clear();
            axis.EndTrackDrag();

            var project = mTrackHeadList.mDependency.ProjectHolder.Value;
            if (project == null || tracks.Count == 0 || delta == 0)
                return;

            // delta 平移：每条被拖轨目标 = 原索引 + delta（升序，故先全移除再按升序目标插入即得正确排布，
            // 非拖拽轨自然填入剩余槽位、被拖轨彼此相对 index 不变）。
            var targets = tracks.Select(t => project.Tracks.IndexOf(t) + delta).ToList();
            foreach (var tr in tracks)
                project.RemoveTrack(tr);
            for (int k = 0; k < tracks.Count; k++)
                project.InsertTrack(targets[k], tracks[k]);
            project.Commit();
        }

        public void CancelTrackHeadDrag()
        {
            if (mDraggedHeads.Count == 0)
                return;

            foreach (var h in mDraggedHeads)
                h.ZIndex = 0;
            mDraggedHeads.Clear();
            mTrackHeadList.mDependency.TrackVerticalAxis.EndTrackDrag();
        }

        readonly List<TrackHead> mDraggedHeads = new();

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

            var project = mTrackHeadList.mDependency.ProjectHolder.Value;
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

                var project = trackHeadList.mDependency.ProjectHolder.Value;
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

        protected override void OnMouseDown(MouseDownEventArgs e)
        {
            // 点轨道头列表空白区（轨道头/新建按钮之外）→ 取消全部轨道选中。
            if (e.MouseButtonType != MouseButtonType.PrimaryButton)
                return;

            mTrackHeadList.mDependency.ProjectHolder.Value?.Tracks.DeselectAllItems();
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Style.INTERFACE.ToBrush(), this.Rect());
            var project = mTrackHeadList.mDependency.ProjectHolder.Value;
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

    readonly ViewRefreshScheduler mTrackListRefresh;
    readonly DisposableManager s = new();

    readonly BackLayer mBackLayer;
    readonly TrackHeadLayer mTrackHeadLayer;
    readonly IDependency mDependency;
}
