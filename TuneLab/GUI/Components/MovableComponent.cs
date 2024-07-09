using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Structures;
using TuneLab.GUI.Input;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

internal class MovableComponent : Component
{
    public IActionEvent MoveStart => mMoveStart;
    public IActionEvent MoveEnd => mMoveEnd;
    public IActionEvent<Avalonia.Point> Moved => mMoved;

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, this.Rect());
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        if (e.MouseButtonType != MouseButtonType.PrimaryButton)
            return;

        mDownOffset = e.Position;
        CorrectCursor();
        mMoveStart.Invoke();
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (!IsPrimaryButtonPressed)
            return;

        mMoved.Invoke(e.Position - mDownOffset + Bounds.Position);
    }

    protected override void OnMouseUp(MouseUpEventArgs e)
    {
        if (e.MouseButtonType != MouseButtonType.PrimaryButton)
            return;

        CorrectCursor();
        mMoveEnd.Invoke();
    }

    protected override void OnMouseEnter(MouseEnterEventArgs e)
    {
        if (IsPressed)
            return;

        CorrectCursor();
    }

    protected override void OnMouseLeave(MouseLeaveEventArgs e)
    {
        if (IsPressed)
            return;

        CorrectCursor();
    }

    void CorrectCursor()
    {
        if (IsPressed)
        {
            // CloseHand
            return;
        }

        if (IsHover)
        {
            // OpenHand
            return;
        }

        Cursor = null;
    }

    Avalonia.Point mDownOffset;

    readonly ActionEvent mMoveStart = new();
    readonly ActionEvent mMoveEnd = new();
    readonly ActionEvent<Avalonia.Point> mMoved = new();
}
