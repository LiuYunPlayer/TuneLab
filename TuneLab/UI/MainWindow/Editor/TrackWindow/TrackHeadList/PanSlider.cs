using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Utils;

using Point = Avalonia.Point;

namespace TuneLab.UI;

internal class PanSlider : AbstractSlider
{
    public PanSlider()
    {
        Thumb = new PanThumb(this);
        // tooltip 跟随显示值合拍更新（单级）：拖动中每帧的值变化并成一拍一次重设。
        mTipRefresh = new(SetupTip);
        ValueDisplayed.Subscribe(mTipRefresh.InvalidateStructure);
        SetupTip();
    }

    private void SetupTip()
    {
        ToolTip.SetPlacement(this, PlacementMode.Top);
        ToolTip.SetVerticalOffset(this, -8);
        ToolTip.SetTip(this,
            Value > 0 ?
            string.Format("R+{0}", Value.ToString("f2")) :
            Value < 0 ?
            string.Format("L+{0}", (-Value).ToString("f2")) :
            "Balance"
            );
    }

    protected override Point GetStartPoint(Size size) => new(0, size.Height / 2);
    protected override Point GetEndPoint(Size size) => new(size.Width, size.Height / 2);

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Style.BACK.ToBrush(), this.Rect());
        if (Thumb == null)
            return;

        double thumbX = Thumb.Bounds.Center.X;
        double center = Bounds.Width / 2;
        double left = Math.Min(thumbX, center);
        double right = Math.Max(thumbX, center);
        context.FillRectangle(Style.HIGH_LIGHT.ToBrush(), new Rect(left, 0, right - left, Bounds.Height));
    }

    protected override void OnSizeChanged(Avalonia.Controls.SizeChangedEventArgs e)
    {
        if (Thumb == null)
            return;

        Thumb.Height = e.NewSize.Height;
        InvalidateArrange();
        SetupTip();
    }

    class PanThumb : AbstractThumb
    {
        public PanThumb(PanSlider slider) : base(slider)
        {
            Width = 2;
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brushes.White, this.Rect());
        }
    }

    readonly ViewRefreshScheduler mTipRefresh;
}
