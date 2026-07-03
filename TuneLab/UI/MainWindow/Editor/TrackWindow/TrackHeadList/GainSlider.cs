using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI.Components;
using TuneLab.GUI;
using TuneLab.Utils;
using Avalonia.Controls;

using Point = Avalonia.Point;

namespace TuneLab.UI;

internal class GainSlider : AbstractSlider
{
    public GainSlider()
    {
        Thumb = new GainThumb(this);
        // tooltip 跟随显示值合拍更新（单级）：拖动中每帧的值变化并成一拍一次重设。
        mTipRefresh = new(SetupTip);
        ValueDisplayed.Subscribe(mTipRefresh.InvalidateStructure);
    }

    private void SetupTip()
    {
        ToolTip.SetPlacement(this, PlacementMode.Top);
        ToolTip.SetVerticalOffset(this, -8);
        ToolTip.SetShowDelay(this, 0);
        var x = ThumbPivotPosition().X;
        ToolTip.SetHorizontalOffset(this, x - Bounds.Width / 2);
        ToolTip.SetTip(this, Value.ToString("+0.00dB;-0.00dB"));
    }

    protected override Point GetStartPoint(Size size) => new(2, size.Height / 2);
    protected override Point GetEndPoint(Size size) => new(size.Width - 2, size.Height / 2);

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, this.Rect());
        const double height = 6;
        context.FillRectangle(Style.BACK.ToBrush(), new Rect(0, (Bounds.Height - height) / 2, Bounds.Width, height));
    }

    protected override void OnSizeChanged(Avalonia.Controls.SizeChangedEventArgs e)
    {
        if (Thumb == null)
            return;

        Thumb.Height = e.NewSize.Height;
        InvalidateArrange();
        SetupTip();
    }

    class GainThumb : AbstractThumb
    {
        public GainThumb(GainSlider slider) : base(slider)
        {
            Width = 4;
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Style.LIGHT_WHITE.ToBrush(), this.Rect(), 1);
        }
    }

    readonly ViewRefreshScheduler mTipRefresh;
}