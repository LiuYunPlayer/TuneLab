using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Animation;
using TuneLab.Base.Properties;
using TuneLab.GUI;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

/// <summary>
/// A capsule-shaped two-state switch control (like a CheckBox, a variant of <see cref="Toggle"/>,
/// but with a pill-shaped UI and a sliding highlight). The caller only needs to configure the
/// <see cref="OffIcon"/> (shown on the left / unchecked side) and <see cref="OnIcon"/>
/// (shown on the right / checked side); the highlight pill slides between the two sides
/// with a smooth easing animation and the icon colors fade between bright / dim.
/// </summary>
internal class Switch : Toggle, IDataValueController<bool>
{
    /// <summary>Icon rendered on the right (checked) side. Brighter when <see cref="Toggle.IsChecked"/> is true.</summary>
    public SvgIcon? OnIcon { get => mOnIcon; set { mOnIcon = value; InvalidateVisual(); } }
    /// <summary>Icon rendered on the left (unchecked) side. Brighter when <see cref="Toggle.IsChecked"/> is false.</summary>
    public SvgIcon? OffIcon { get => mOffIcon; set { mOffIcon = value; InvalidateVisual(); } }

    /// <summary>Background (capsule) color.</summary>
    public Color BackColor { get => mBackColor; set { mBackColor = value; InvalidateVisual(); } }
    /// <summary>Moving highlight pill color.</summary>
    public Color HighlightColor { get => mHighlightColor; set { mHighlightColor = value; InvalidateVisual(); } }
    /// <summary>Color used for the icon on the currently selected side.</summary>
    public Color ActiveIconColor { get => mActiveIconColor; set { mActiveIconColor = value; SnapIconColors(); } }
    /// <summary>Color used for the icon on the non-selected side.</summary>
    public Color InactiveIconColor { get => mInactiveIconColor; set { mInactiveIconColor = value; SnapIconColors(); } }
    /// <summary>Padding between the background capsule and the highlight pill.</summary>
    public double InnerPadding { get; set; } = 2.0;

    public Switch()
    {
        Width = 40;
        Height = 20;

        mPosition = new AnimationValue { Value = TargetPosition() };
        mOnColor = new AnimationColor { Value = TargetOnColor() };
        mOffColor = new AnimationColor { Value = TargetOffColor() };

        mPosition.ValueChanged += InvalidateVisual;
        mOnColor.ValueChanged += InvalidateVisual;
        mOffColor.ValueChanged += InvalidateVisual;

        // Trigger animations whenever the checked state flips (via click or IsChecked setter).
        Switched.Subscribe(OnSwitched);
    }

    /// <summary>
    /// Hides <see cref="Toggle.Display(bool)"/> so that programmatic state changes (e.g. initial
    /// synchronization from a data source) snap to the final visual state without animating.
    /// </summary>
    public new void Display(bool value)
    {
        base.Display(value);
        // Snap animations to the final state (millisec = 0 => no interpolation).
        mPosition.SetTo(TargetPosition(), 0);
        mOnColor.SetTo(TargetOnColor(), 0);
        mOffColor.SetTo(TargetOffColor(), 0);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var rect = this.Rect();
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var radius = rect.Height / 2.0;

        // 1) Background capsule.
        context.DrawRectangle(mBackColor.ToBrush(), null, rect, radius, radius);

        // 2) Sliding highlight pill (covers exactly half of the control width, inset by InnerPadding).
        var halfWidth = rect.Width / 2.0;
        var padding = InnerPadding;
        var hw = halfWidth - 2 * padding;
        var hh = rect.Height - 2 * padding;
        if (hw > 0 && hh > 0)
        {
            var hx = rect.X + padding + mPosition.Value * halfWidth;
            var hy = rect.Y + padding;
            var hRadius = hh / 2.0;
            context.DrawRectangle(mHighlightColor.ToBrush(), null, new Rect(hx, hy, hw, hh), hRadius, hRadius);
        }

        // 3) Icons on top: OffIcon centered in the left half, OnIcon centered in the right half.
        PaintIcon(context, mOffIcon, new Rect(rect.X, rect.Y, halfWidth, rect.Height), mOffColor.Value);
        PaintIcon(context, mOnIcon, new Rect(rect.X + halfWidth, rect.Y, halfWidth, rect.Height), mOnColor.Value);
    }

    void OnSwitched()
    {
        mPosition.SetTo(TargetPosition(), AnimationMillisec, AnimationCurve.QuadOut);
        mOnColor.SetTo(TargetOnColor(), AnimationMillisec, AnimationCurve.QuadOut);
        mOffColor.SetTo(TargetOffColor(), AnimationMillisec, AnimationCurve.QuadOut);
    }

    void SnapIconColors()
    {
        mOnColor.SetTo(TargetOnColor(), 0);
        mOffColor.SetTo(TargetOffColor(), 0);
        InvalidateVisual();
    }

    double TargetPosition() => IsChecked ? 1.0 : 0.0;
    Color TargetOnColor() => IsChecked ? mActiveIconColor : mInactiveIconColor;
    Color TargetOffColor() => IsChecked ? mInactiveIconColor : mActiveIconColor;

    static void PaintIcon(DrawingContext context, SvgIcon? icon, Rect rect, Color color)
    {
        if (icon == null)
            return;

        var image = icon.GetImage(color);
        var size = image.Size;
        var x = rect.X + (rect.Width - size.Width) / 2;
        var y = rect.Y + (rect.Height - size.Height) / 2;
        context.DrawImage(image, new Rect(x, y, size.Width, size.Height));
    }

    SvgIcon? mOnIcon;
    SvgIcon? mOffIcon;
    Color mBackColor = Style.BACK;
    Color mHighlightColor = Style.HIGH_LIGHT;
    Color mActiveIconColor = Style.WHITE;
    Color mInactiveIconColor = Style.LIGHT_WHITE.Opacity(0.5);

    readonly AnimationValue mPosition;
    readonly AnimationColor mOnColor;
    readonly AnimationColor mOffColor;
}
