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

namespace TuneLab.Views;

internal class GainSlider : AbstractSlider
{
    public GainSlider()
    {
        Thumb = new GainThumb(this);
    }
    public Tuple<double, double> RealtimeAmplitude { get; set; } = new Tuple<double, double>(0, 0);
    protected override Point StartPoint => new(2, 0);
    protected override Point EndPoint => new(Bounds.Width - 2, 0);

    public void Update()
    {
        System.Reflection.MethodInfo method = this.GetType().BaseType.GetMethod("RefreshUI",System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Invoke(this, null);
    }

    protected override void OnDraw(DrawingContext context)
    {
        context.FillRectangle(Style.HIGH_LIGHT.ToBrush(), new Rect(0, 0, this.Rect().Width * RealtimeAmplitude.Item1.Limit(0, 1), this.Rect().Height / 2));//Left
        context.FillRectangle(Style.HIGH_LIGHT.ToBrush(), new Rect(0, this.Rect().Height / 2, this.Rect().Width * RealtimeAmplitude.Item2.Limit(0, 2), this.Rect().Height / 2));//Right

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
}