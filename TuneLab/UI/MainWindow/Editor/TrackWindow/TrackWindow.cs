using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.Input;
using TuneLab.Utils;
using TuneLab.SDK;
using TuneLab.I18N;

namespace TuneLab.UI;

internal class TrackWindow : DockPanel, TimelineView.IDependency, TrackScrollView.IDependency, PlayheadLayer.IDependency, RegionSelectionLayer.IDependency, TrackVerticalAxis.IDependency, TrackHeadList.IDependency
{
    public IHolder<IProject> ProjectHolder => mDependency.ProjectHolder;
    public IHolder<ITimeline> TimelineHolder => mDependency.ProjectHolder;
    public IQuantization Quantization => mQuantization;
    public TickAxis TickAxis => mTickAxis;
    public TrackVerticalAxis TrackVerticalAxis => mTrackVerticalAxis;
    public IPlayhead Playhead => mDependency.Playhead;
    public IHolder<IPart> EditingPart => mDependency.EditingPart;
    public TickAxis PianoTickAxis => mDependency.PianoTickAxis;
    public PitchAxis PianoPitchAxis => mDependency.PianoPitchAxis;
    public IProject? Project => ProjectHolder.Value;
    public TrackScrollView TrackScrollView => mTrackScrollView;
    public INotifiableProperty<PlayScrollTarget> PlayScrollTarget => mDependency.PlayScrollTarget;

    public interface IDependency
    {
        IHolder<IProject> ProjectHolder { get; }
        IPlayhead Playhead { get; }
        IHolder<IPart> EditingPart { get; }
        TickAxis PianoTickAxis { get; }
        PitchAxis PianoPitchAxis { get; }
        void SwitchEditingPart(IPart? part);
        INotifiableProperty<PlayScrollTarget> PlayScrollTarget { get; }
    }

    public TrackWindow(IDependency dependency)
    {
        mDependency = dependency;

        mQuantization = new(MusicTheory.QuantizationBase.Base_1, MusicTheory.QuantizationDivision.Division_8);
        mTickAxis = new();
        mTrackVerticalAxis = new(this);

        mTrackHeadList = new(this) { Width = 232, Margin = new(1, 0, 0, 0) };
        var headArea = new DockPanel();
        {
            var title = new DockPanel() { Height = 48, Background = Style.INTERFACE.ToBrush(), Margin = new(1, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            {
                var icon = new Image() { Source = Assets.Track.GetImage(Style.LIGHT_WHITE), Width = 24, Height = 24, Margin = new(16, 12, 12, 12) };
                title.AddDock(icon, Dock.Left);
                var name = new Label() { Content = "Track".Tr(TC.Dialog), FontSize = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.TEXT_LIGHT.ToBrush() };
                title.AddDock(name);
            }
            headArea.AddDock(title, Dock.Top);
            headArea.AddDock(new Border() { Height = 1, Background = Style.BACK.ToBrush() }, Dock.Top);
            headArea.AddDock(mTrackHeadList);
        }
        this.AddDock(headArea, Dock.Right);

        var layerPanel = new LayerPanel();
        {
            var layer = new DockPanel();
            {
                mTimelineView = new(this) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
                layer.AddDock(mTimelineView, Dock.Top);

                layer.AddDock(new Border() { Height = 1, Background = Style.BACK.ToBrush() }, Dock.Top);

                mTrackScrollView = new(this);
                // TrackScrollView 与范围选区覆盖层同区叠放：覆盖层压在 parts 之上、共用同一坐标系（GetTop 无需偏移）。
                // ClipToBounds：与钢琴窗一致，裁掉子级（尤其压低高度时横向条手柄）溢出到本区之外、盖住上方 timeline。
                var scrollArea = new LayerPanel() { ClipToBounds = true };
                scrollArea.Children.Add(mTrackScrollView);
                mRegionSelectionLayer = new(this);
                scrollArea.Children.Add(mRegionSelectionLayer);
                // 滚动条：横向绑时间轴（无界，设 ContentExtentProvider = 内容末尾口径）、纵向绑轨道轴（有界）。
                // 铺满 scrollArea 但只手柄可命中、其余穿透（不抢 parts 指针事件）。
                mHorizontalScrollBar = new(mTickAxis, Orientation.Horizontal) { ContentExtentProvider = GetContentEndX };
                mVerticalScrollBar = new(mTrackVerticalAxis, Orientation.Vertical);
                scrollArea.Children.Add(mHorizontalScrollBar);
                scrollArea.Children.Add(mVerticalScrollBar);
                // 靠近边缘才显示：view 层职责（普通控件默认常驻，此处按编排区需求加显隐）。
                mHorizontalReveal = new(mHorizontalScrollBar, scrollArea, Orientation.Horizontal);
                mVerticalReveal = new(mVerticalScrollBar, scrollArea, Orientation.Vertical);
                layer.AddDock(scrollArea);
            }
            layerPanel.Children.Add(layer);

            mPlayheadLayer = new(this);
            layerPanel.Children.Add(mPlayheadLayer);
        }
        this.AddDock(layerPanel);

        TickAxis.ScaleLevel = -8;
    }

    public void SwitchEditingPart(IPart part)
    {
        mDependency.SwitchEditingPart(part);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        mTickAxis.ViewLength = e.NewSize.Width - mTrackHeadList.Width;
        mTrackVerticalAxis.ViewLength = e.NewSize.Height - mTimelineView.Height;
        mTrackVerticalAxis.RefreshContentSize();   // 可滚动范围含一个视图高余量，随视图高变化重算
    }

    // 编排区作用域的命令分发（TrackWindow 域）。焦点落在编排区子树时本控件先收到键、命中即截停，未命中自然
    // 冒泡到 Editor。目前 TrackWindow 域承载 track/part/trackContent 上下文的脚本命令（内建命令暂无此域）；
    // 剪贴板类 edit.* 仍在 Editor 域、由 Editor 按聚焦面路由（不在此匹配，故此处 false 后照常冒泡）。
    // 仅在无进行中操作时分发——与钢琴窗一致。
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.IsHandledByTextBox())
            return;
        if (TrackScrollView.OperationState != TrackScrollView.State.None)
            return;
        e.Handled = Keymap.TryHandle(KeyScope.TrackWindow, e);
    }

    // 剪贴板类命令（复制/剪切/粘贴/删除/全选）是与钢琴窗共享的通用动作，注册在 Editor 域、由 Editor 按聚焦面
    // 路由到下列 *Selection 方法（不由本控件的 OnKeyDown 分发）。有范围选区时作用于选区（闸刀语义，与选区
    // 右键菜单一致）、否则作用于选中的整块 part。仅在无进行中操作时生效——原本由 OnKeyDown 前置守卫，此处自守。
    bool CanRunEditCommand => TrackScrollView.OperationState == TrackScrollView.State.None;

    public void CopySelection()
    {
        if (!CanRunEditCommand) return;
        if (TrackScrollView.CurrentSelection is { } region) TrackScrollView.CopyRegion(region);
        else TrackScrollView.Copy();
    }

    public void CutSelection()
    {
        if (!CanRunEditCommand) return;
        if (TrackScrollView.CurrentSelection is { } region) TrackScrollView.CutRegion(region);
        else TrackScrollView.Cut();
    }

    public void PasteSelection()
    {
        if (!CanRunEditCommand) return;
        TrackScrollView.PasteAt(TrackScrollView.GetQuantizedTick(Playhead.Pos));
    }

    public void DeleteSelection()
    {
        if (!CanRunEditCommand) return;
        if (TrackScrollView.CurrentSelection is { } region) TrackScrollView.DeleteRegion(region);
        else TrackScrollView.DeleteAllSelectedParts();
    }

    public void SelectAllInTrack()
    {
        if (!CanRunEditCommand) return;
        Project?.Tracks.SelectMany(track => track.Parts).SelectAllItems();
    }

    readonly Quantization mQuantization;
    readonly TickAxis mTickAxis;
    readonly TrackVerticalAxis mTrackVerticalAxis;

    // 横向滚动条的内容末尾像素长度：所有 part 的最大末尾 tick × 每 tick 像素（手柄滑到底 = 视口远边正好
    // 落在内容末尾；无限拖仍可继续拖过末尾、手柄钳在边缘）。
    double GetContentEndX()
    {
        var project = Project;
        if (project == null)
            return 0;

        double maxEndTick = 0;
        foreach (var track in project.Tracks)
            foreach (var part in track.Parts)
                maxEndTick = Math.Max(maxEndTick, part.EndPos());

        return maxEndTick * mTickAxis.PixelsPerTick;
    }

    readonly TrackHeadList mTrackHeadList;
    readonly TimelineView mTimelineView;
    readonly TrackScrollView mTrackScrollView;
    readonly RegionSelectionLayer mRegionSelectionLayer;
    readonly PlayheadLayer mPlayheadLayer;
    readonly ScrollBar mHorizontalScrollBar;
    readonly ScrollBar mVerticalScrollBar;
    readonly EdgeProximityReveal mHorizontalReveal;
    readonly EdgeProximityReveal mVerticalReveal;

    readonly IDependency mDependency;
}
