using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.Views;

internal class PanSlider : AbstractSlider
{
    public PanSlider()
    {
        Thumb = new PanThumb(this);
    }

    protected override Point StartPoint => new(0, 0);
    protected override Point EndPoint => new(Bounds.Width, 0);

    protected override void OnDraw(DrawingContext context)
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
}
