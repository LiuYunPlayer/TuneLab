using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TuneLab.GUI.Input;
using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Avalonia.Svg.Skia;
using Svg.Skia;
using System.IO;
using TuneLab.Utils;
using TuneLab.Animation;

namespace TuneLab.Views;

internal class ParameterButton : Panel
{
    public event Action<ButtonState>? StateChangeAsked;
    public event Action? StateChanged;
    public Color Color
    {
        get => mColor;
        [MemberNotNull(nameof(mColor))]
        [MemberNotNull(nameof(mDarkColor))]
        [MemberNotNull(nameof(mEyeColor))]
        [MemberNotNull(nameof(mBeforeEyeColor))]
        [MemberNotNull(nameof(mAfterEyeColor))]
        [MemberNotNull(nameof(mBeforeButtonColor))]
        [MemberNotNull(nameof(mAfterButtonColor))]
        set
        {
            if (mColorProgressAnimation.IsPlaying)
                mColorProgressAnimation.Stop();

            mColor = value;
            var hsv = mColor.ToHsv();
            mDarkColor = new HsvColor(1, hsv.H, hsv.S, hsv.V * 2 / 3).ToRgb();

            mEyeColor = EyeColor(State);
            mBeforeEyeColor = mEyeColor;
            mAfterEyeColor = mEyeColor;
            var backColor = BackColor(State, misHover);
            mBeforeButtonColor = backColor;
            mAfterButtonColor = backColor;

            CorrectColor(0);
        }
    }
    public string Text
    {
        get => mText;
        set
        {
            mText = value;
            mLabel.Text = mText;
        }
    }

    public enum ButtonState
    {
        Off,
        Visible,
        Edit,
    }

    public ButtonState State
    {
        get => mState;
        set
        {
            if (mState == value)
                return;

            mState = value;
            AnimateColor();
            StateChanged?.Invoke();
        }
    }

    public ParameterButton(Color color, string name)
    {
        mButtonBack = new();
        Children.Add(mButtonBack);

        mLabel = new() { Foreground = Brushes.White, FontSize = 12, Padding = new Thickness(26, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        Children.Add(mLabel);
        mImage = new() { IsHitTestVisible = false };
        Children.Add(mImage);

        mButtonBack.EditAsked += () =>
        {
            StateChangeAsked?.Invoke(State == ButtonState.Edit ? ButtonState.Visible : ButtonState.Edit);
        };

        mButtonBack.VisibleAsked += () =>
        {
            if (State != ButtonState.Edit)
            {
                StateChangeAsked?.Invoke(State == ButtonState.Off ? ButtonState.Visible : ButtonState.Off);
            }
        };

        mButtonBack.HoverStateChanged += (isHover) =>
        {
            misHover = isHover;
            AnimateColor();
        };

        mColorProgressAnimation.ValueChanged += CorrectColor;

        Color = color;
        Text = name;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        mLabel.Measure(availableSize);
        return mLabel.DesiredSize.WithHeight(24);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        mButtonBack.Arrange(new Rect(finalSize));
        mLabel.Arrange(new Rect(finalSize));
        mImage.Arrange(new Rect(10, 7, 12, 10));
        return finalSize;
    }

    Color BackColor(ButtonState state, bool isHover)
    {
        return state == ButtonState.Edit ? mDarkColor : isHover ? HoverColor : Colors.Transparent;
    }

    Color EyeColor(ButtonState state)
    {
        return state == ButtonState.Visible ? mColor : EyeOffColor;
    }

    void AnimateColor()
    {
        mBeforeEyeColor = mEyeColor;
        mBeforeButtonColor = mButtonBack.Color;
        mAfterEyeColor = EyeColor(State);
        mAfterButtonColor = BackColor(State, misHover);
        mColorProgressAnimation.SetFromTo(0, 1, 150, AnimationCurve.QuadOut);
    }

    void CorrectColor(double progress)
    {
        mEyeColor = mBeforeEyeColor.Lerp(mAfterEyeColor, progress);
        mImage.Source = EyeImage(mEyeColor);
        mButtonBack.Color = mBeforeButtonColor.Lerp(mAfterButtonColor, progress);
    }

    class ButtonBack : Control
    {
        public event Action? EditAsked;
        public event Action? VisibleAsked;
        public event Action<bool>? HoverStateChanged;
        public Color Color
        {
            get => mColor;
            set { mColor = value; InvalidateVisual(); }
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Color.ToBrush(), this.Rect(), 4);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var p = e.GetCurrentPoint(this);
            switch (p.Properties.PointerUpdateKind.ToMouseButtonType())
            {
                case MouseButtonType.PrimaryButton:
                    EditAsked?.Invoke();
                    break;
                case MouseButtonType.SecondaryButton:
                    VisibleAsked?.Invoke();
                    break;
                default:
                    break;
            }
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            HoverStateChanged?.Invoke(true);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            HoverStateChanged?.Invoke(false);
        }

        Color mColor = Colors.Transparent;
    }

    static SvgImage? EyeImage(Color color)
    {
        return GUI.Assets.Eye.GetImage(color);
    }

    static readonly Color HoverColor = new Color(12, 255, 255, 255);
    static readonly Color EyeOffColor = new Color(102, 255, 255, 255);

    AnimationController mColorProgressAnimation = new();
    ButtonState mState = ButtonState.Off;
    bool misHover = false;

    Color mEyeColor;
    Color mBeforeEyeColor;
    Color mAfterEyeColor;
    Color mBeforeButtonColor;
    Color mAfterButtonColor;

    Color mColor;
    Color mDarkColor;
    string mText = string.Empty;

    readonly TextBlock mLabel;
    readonly Image mImage;
    //readonly IconTintColorBehavior mImageBehavior = new();
    readonly ButtonBack mButtonBack;
}
