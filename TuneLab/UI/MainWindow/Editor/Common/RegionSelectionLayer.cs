using Avalonia;
using Avalonia.Media;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.UI;

// 编排区范围选区的覆盖层：单独叠在 TrackScrollView（parts）之上画，避免常驻选区被 part 遮挡。
// 与 TrackScrollView 同区叠放（共用 LayerPanel），故 TrackVerticalAxis 的 Y 坐标无需偏移即对齐。
// 选区状态仍归 TrackScrollView，本层只读 CurrentSelection、订阅 SelectionChanged + 轴变化重绘。
internal class RegionSelectionLayer : Component
{
    public interface IDependency
    {
        TickAxis TickAxis { get; }
        TrackVerticalAxis TrackVerticalAxis { get; }
        TrackScrollView TrackScrollView { get; }
    }

    public RegionSelectionLayer(IDependency dependency)
    {
        mDependency = dependency;

        IsHitTestVisible = false;   // 纯绘制层，鼠标穿透到下面的 TrackScrollView

        TrackScrollView.SelectionChanged += InvalidateVisual;
        TickAxis.AxisChanged += InvalidateVisual;
        TrackVerticalAxis.AxisChanged += InvalidateVisual;
    }

    ~RegionSelectionLayer()
    {
        TrackScrollView.SelectionChanged -= InvalidateVisual;
        TickAxis.AxisChanged -= InvalidateVisual;
        TrackVerticalAxis.AxisChanged -= InvalidateVisual;
    }

    public override void Render(DrawingContext context)
    {
        if (TrackScrollView.CurrentSelection is not { } selection)
            return;

        double left = TickAxis.Tick2X(selection.StartTick);
        double right = TickAxis.Tick2X(selection.EndTick);
        double top = TrackVerticalAxis.GetTop(selection.StartTrackIndex);
        double bottom = TrackVerticalAxis.GetBottom(selection.EndTrackIndex);
        var rect = new Rect(left, top, right - left, bottom - top);

        // 白色提亮 wash + 白虚线边框：白是唯一 hue-neutral 的色（轨道色绕满色相环，任何带色相的填充都会撞某条轨）。
        // 靠"区域级整片罩白 + 虚线框(marching-ants)"与"选中 part 的对象级白实线 2px 圆角框"按形态/线型区分，不靠色相。
        var pen = new Pen(SelectionColor.ToUInt32(), 1) { DashStyle = DashStyle.Dash };   // 纯白实色虚线，立住边界
        context.DrawRectangle(SelectionColor.Opacity(0.12).ToBrush(), pen, rect);
    }

    static readonly Color SelectionColor = Style.WHITE;   // 白色系：hue-neutral，对任何轨道色都成立

    TickAxis TickAxis => mDependency.TickAxis;
    TrackVerticalAxis TrackVerticalAxis => mDependency.TrackVerticalAxis;
    TrackScrollView TrackScrollView => mDependency.TrackScrollView;

    readonly IDependency mDependency;
}
