using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

internal class Component : Control
{
    public ModifierKeys Modifiers => mLastKeyModifiers;
    public Avalonia.Point MousePosition => mLastMousePosition;
    public bool IsHover => mHoverComponent == this;
    public bool IsPrimaryButtonPressed => mIsPrimaryButtonPressed;
    public bool IsMiddleButtonPressed => mIsMiddleButtonPressed;
    public bool IsSecondaryButtonPressed => mIsSecondaryButtonPressed;
    public bool IsPressed => mIsPrimaryButtonPressed || mIsMiddleButtonPressed || mIsSecondaryButtonPressed;
    public long DoubleClickInterval { get; set; } = 300;

    public Component()
    {
        Focusable = true;
        IsTabStop = false;
        ClipToBounds = true;
    }

    static Component()
    {
        mStopwatch.Start();
    }

    protected virtual void OnScroll(WheelEventArgs e) { }
    protected virtual void OnMouseDown(MouseDownEventArgs e) { }
    protected virtual void OnMouseMove(MouseMoveEventArgs e) { }
    protected virtual void OnMouseUp(MouseUpEventArgs e) { }
    protected virtual void OnMouseEnter(MouseEnterEventArgs e) { }
    protected virtual void OnMouseLeave(MouseLeaveEventArgs e) { }
    protected virtual void OnKeyDownEvent(KeyEventArgs e) { }
    protected virtual void OnKeyPressedEvent(KeyEventArgs e) { }
    protected virtual void OnKeyUpEvent(KeyEventArgs e) { }

    protected sealed override void OnKeyDown(KeyEventArgs e)
    {
        mLastKeyModifiers = e.ModifierKeys();
        if (e.IsHandledByTextBox())
            return;

        if (mFirstKeyDown)
            OnKeyDownEvent(e);
        else
            OnKeyPressedEvent(e);

        mFirstKeyDown = false;
    }

    protected sealed override void OnKeyUp(KeyEventArgs e)
    {
        mLastKeyModifiers = e.ModifierKeys();
        OnKeyUpEvent(e);
        mFirstKeyDown = true;
    }

    protected sealed override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        CallMouseDown(new()
        {
            KeyModifiers = e.KeyModifiers.ToModifierKeys(),
            Position = p.Position,
            MouseButtonType = p.Properties.PointerUpdateKind.ToMouseButtonType(),
        });
    }

    protected sealed override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        switch (p.Properties.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonPressed:
            case PointerUpdateKind.MiddleButtonPressed:
            case PointerUpdateKind.RightButtonPressed:
                CallMouseDown(new()
                {
                    KeyModifiers = e.KeyModifiers.ToModifierKeys(),
                    Position = p.Position,
                    MouseButtonType = p.Properties.PointerUpdateKind.ToMouseButtonType(),
                });
                break;
            case PointerUpdateKind.LeftButtonReleased:
            case PointerUpdateKind.MiddleButtonReleased:
            case PointerUpdateKind.RightButtonReleased:
                CallMouseUp(new()
                {
                    KeyModifiers = e.KeyModifiers.ToModifierKeys(),
                    Position = p.Position,
                    MouseButtonType = p.Properties.PointerUpdateKind.ToMouseButtonType(),
                });
                break;
            default:
                var args = new MouseMoveEventArgs()
                {
                    KeyModifiers = e.KeyModifiers.ToModifierKeys(),
                    Position = p.Position,
                };
                // 一些情况比如设置tooltip时会异常触发此事件，这里用与上次触发此事件时鼠标的绝对位置的比较来避免这种异常事件
                var globalPosition = e.GetPosition(null);
                if (globalPosition.FuzzyEquals(mLastMoveGlobalPosition))
                    break;

                CallMouseMove(args);
                mLastMoveGlobalPosition = globalPosition;
                break;
        }
    }

    protected sealed override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        CallMouseUp(new()
        {
            KeyModifiers = e.KeyModifiers.ToModifierKeys(),
            Position = p.Position,
            MouseButtonType = p.Properties.PointerUpdateKind.ToMouseButtonType(),
        });
    }

    protected sealed override void OnPointerEntered(PointerEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        CallMouseEnter(new()
        {
            KeyModifiers = e.KeyModifiers.ToModifierKeys(),
            Position = p.Position,
        });
    }

    protected sealed override void OnPointerExited(PointerEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        CallMouseLeave(new()
        {
            KeyModifiers = e.KeyModifiers.ToModifierKeys(),
            Position = p.Position,
        });
    }

    protected sealed override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        OnScroll(new()
        {
            KeyModifiers = e.KeyModifiers.ToModifierKeys(),
            Position = p.Position,
            Delta = e.Delta.ToAvaloniaPoint(),
        });
    }

    // 捕获丢失（按着鼠标时切走窗口、系统弹窗抢走鼠标……）：松开再也不会送到这里，补发抬起，让拖动照常收尾、提交——
    // 否则拖动状态一直挂着：值控件的撤销步骤合不上、所在窗口一直算"编辑中"（属性面板停止刷新，见 EditGate），
    // 要等用户回来在同一个控件上再点一下才解开。
    // 推迟一拍再判：正常松开时 Avalonia 也会在 PointerReleased 之后释放捕获——让真正的抬起先到、清掉按键状态，这里就什么都不做。
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Dispatcher.UIThread.Post(ReleasePressedButtons);
    }

    void ReleasePressedButtons()
    {
        if (mIsPrimaryButtonPressed) CallMouseUp(SynthesizedUp(MouseButtonType.PrimaryButton), synthesized: true);
        if (mIsMiddleButtonPressed) CallMouseUp(SynthesizedUp(MouseButtonType.MiddleButton), synthesized: true);
        if (mIsSecondaryButtonPressed) CallMouseUp(SynthesizedUp(MouseButtonType.SecondaryButton), synthesized: true);
    }

    MouseUpEventArgs SynthesizedUp(MouseButtonType button) => new()
    {
        KeyModifiers = mLastKeyModifiers,
        Position = mLastMousePosition,
        MouseButtonType = button,
    };

    void CallMouseDown(MouseDownEventArgs e)
    {
        mLastMousePosition = e.Position;
        mDownPosition = e.Position;
        e.IsDoubleClick = mLastClickPosition == e.Position && mStopwatch.ElapsedMilliseconds - mLastClickTime <= DoubleClickInterval;

        switch (e.MouseButtonType)
        {
            case MouseButtonType.PrimaryButton:
                mIsPrimaryButtonPressed = true;
                break;
            case MouseButtonType.MiddleButton:
                mIsMiddleButtonPressed = true;
                break;
            case MouseButtonType.SecondaryButton:
                mIsSecondaryButtonPressed = true;
                break;
        }

        OnMouseDown(e);
    }

    void CallMouseMove(MouseMoveEventArgs e)
    {
        mLastMousePosition = e.Position;
        OnMouseMove(e);
    }

    // synthesized：补发的抬起（见 OnPointerCaptureLost）——恒不算点击，也不参与双击计时，免得误触发点击动作。
    void CallMouseUp(MouseUpEventArgs e, bool synthesized = false)
    {
        if (synthesized)
        {
            e.IsClick = false;
        }
        else
        {
            mLastClickTime = mStopwatch.ElapsedMilliseconds;
            e.IsClick = e.Position.FuzzyEquals(mDownPosition);
            if (e.IsClick) mLastClickPosition = e.Position;
        }

        switch (e.MouseButtonType)
        {
            case MouseButtonType.PrimaryButton:
                mIsPrimaryButtonPressed = false;
                break;
            case MouseButtonType.MiddleButton:
                mIsMiddleButtonPressed = false;
                break;
            case MouseButtonType.SecondaryButton:
                mIsSecondaryButtonPressed = false;
                break;
        }

        OnMouseUp(e);
    }

    void CallMouseEnter(MouseEnterEventArgs e)
    {
        mHoverComponent = this;
        mLastMousePosition = e.Position;
        OnMouseEnter(e);
    }

    void CallMouseLeave(MouseLeaveEventArgs e)
    {
        mHoverComponent = null;
        mLastMousePosition = e.Position;
        OnMouseLeave(e);
    }

    static Component? mHoverComponent = null;
    bool mIsPrimaryButtonPressed = false;
    bool mIsMiddleButtonPressed = false;
    bool mIsSecondaryButtonPressed = false;
    static Stopwatch mStopwatch = new();
    static long mLastClickTime = 0;
    static Avalonia.Point mLastClickPosition;
    static bool mFirstKeyDown = true;
    static ModifierKeys mLastKeyModifiers = ModifierKeys.None;
    Avalonia.Point mLastMousePosition = new(-1, -1);
    static Avalonia.Point mDownPosition;
    static Avalonia.Point mLastMoveGlobalPosition = new(-1, -1);
}
