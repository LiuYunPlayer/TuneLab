using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.UI;

internal class PanSlider : AbstractSlider
{
    public PanSlider()
    {
        Thumb = new PanThumb(this);
        mDirtyHandler.OnReset += SetupTip;
        mDirtyHandler.OnDirty += () =>
        {
            Dispatcher.UIThread.Post(mDirtyHandler.Reset, DispatcherPriority.Normal);
        };
        ValueDisplayed.Subscribe(mDirtyHandler.SetDirty);
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

    protected override Point StartPoint => new(0, Bounds.Height / 2);
    protected override Point EndPoint => new(Bounds.Width, Bounds.Height / 2);

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

    DirtyHandler mDirtyHandler = new();
}
