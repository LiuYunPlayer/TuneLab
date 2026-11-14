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

namespace TuneLab.UI;

internal class ParameterButton : Panel
{
    public event Action<ButtonState>? StateChangeAsked;
    // 激活着时右键：取消激活、作为背景显示（小眼睛打开）。
    public event Action? ShowInBackgroundAsked;
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

    // 单击（松开）时才激活，而不是一按下就激活：标签能拖动排序时，按下可能是开始拖动（见 PianoOverlayStrip）。
    // 拖动开始时拖动方拿走鼠标捕获，这次按下就不算单击。
    public bool EditOnClick { get => mButtonBack.EditOnClick; set => mButtonBack.EditOnClick = value; }

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
            // 右键 = 小眼睛。激活着时右键 = 退回背景并开着小眼睛（相当于左键再点一下 + 打开小眼睛）。
            if (State == ButtonState.Edit)
                ShowInBackgroundAsked?.Invoke();
            else
                StateChangeAsked?.Invoke(State == ButtonState.Off ? ButtonState.Visible : ButtonState.Off);
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
        public bool EditOnClick { get; set; }
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
                    if (EditOnClick)
                        mEditPending = true;
                    else
                        EditAsked?.Invoke();
                    break;
                case MouseButtonType.SecondaryButton:
                    VisibleAsked?.Invoke();
                    break;
                default:
                    break;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!mEditPending)
                return;
            mEditPending = false;
            var p = e.GetPosition(this);
            if (p.X >= 0 && p.Y >= 0 && p.X < Bounds.Width && p.Y < Bounds.Height)
                EditAsked?.Invoke();
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            mEditPending = false;   // 别人拿走了捕获（开始拖动了）：这次按下不算单击
        }

        bool mEditPending;

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
