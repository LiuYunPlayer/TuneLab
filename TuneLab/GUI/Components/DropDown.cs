using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Linq;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

internal class DropDown : ComboBox
{
    public new IBrush? Background { get => base.Background; set { base.Background = value; RefreshStyles(); } }
    public new Thickness BorderThickness { get => base.BorderThickness; set { base.BorderThickness = value; RefreshStyles(); } }
    protected override Type StyleKeyOverride => typeof(ComboBox);

    public DropDown()
    {
        Background = Style.BACK.ToBrush();
        BorderThickness = new(0);

        CornerRadius = new(4);
        FontSize = 12;
        Foreground = Style.LIGHT_WHITE.ToBrush();
        PlaceholderForeground = Style.LIGHT_WHITE.ToBrush();

        Focusable = false;

        mStyles = new(this);
        Styles.Add(mStyles);
    }

    class DropDownStyles : Styles
    {
        public DropDownStyles(DropDown dropDown)
        {
            Add(new Avalonia.Styling.Style(x => x.OfType<ComboBox>().Class(":focus").Template().OfType<Border>().Name("Background"))
            {
                Setters = {
                new Setter(BorderThicknessProperty, dropDown.BorderThickness),
                new Setter(BackgroundProperty, dropDown.Background),
            }
            });
            Add(new Avalonia.Styling.Style(x => x.OfType<ComboBox>().Class(":pointerover").Template().OfType<Border>().Name("Background"))
            {
                Setters = {
                new Setter(BorderThicknessProperty, dropDown.BorderThickness),
                new Setter(BackgroundProperty, dropDown.Background),
            }
            });
            Add(new Avalonia.Styling.Style(x => x.OfType<ComboBox>().Class(":pressed").Template().OfType<Border>().Name("Background"))
            {
                Setters = {
                new Setter(BorderThicknessProperty, dropDown.BorderThickness),
                new Setter(BackgroundProperty, dropDown.Background),
            }
            });
        }
    }

    void RefreshStyles()
    {
        Styles.Remove(mStyles);
        mStyles = new(this);
        Styles.Add(mStyles);
    }

    DropDownStyles mStyles;
}
