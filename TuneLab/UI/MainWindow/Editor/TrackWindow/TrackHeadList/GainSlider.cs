using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI.Components;
using TuneLab.GUI;
using TuneLab.Utils;
using TuneLab.Base.Science;
using Avalonia.Controls;
using TuneLab.Base.Utils;
using Avalonia.Threading;

namespace TuneLab.UI;

internal class GainSlider : AbstractSlider
{
    public GainSlider()
    {
        Thumb = new GainThumb(this);
        mDirtyHandler.OnReset += SetupTip;
        mDirtyHandler.OnDirty += () =>
        {
            Dispatcher.UIThread.Post(mDirtyHandler.Reset, DispatcherPriority.Normal);
        };
        ValueDisplayed.Subscribe(mDirtyHandler.SetDirty);
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

    protected override Point StartPoint => new(2, Bounds.Height / 2);
    protected override Point EndPoint => new(Bounds.Width - 2, Bounds.Height / 2);

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

    DirtyHandler mDirtyHandler = new();
}