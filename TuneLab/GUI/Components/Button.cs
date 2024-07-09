using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI;
using TuneLab.GUI.Input;
using TuneLab.Animation;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

internal class ButtonContent
{
    public event Action? Update;
    public required IItem Item { get => mItem; set { mItem = value; Update?.Invoke(); } }
    public ColorSet ColorSet;

    IItem mItem;
}

internal class Button : Component
{
    public event Action? Clicked;
    public event Action? Pressed;

    public int AnimationMillisec { get; set; } = 150;

    public Button()
    {

    }

    public Button AddContent(ButtonContent content)
    {
        content.Update += InvalidateVisual;
        var c = new ButtonContentController(content);
        c.Color.ValueChanged += InvalidateVisual;
        mButtonContentControllers.Add(c);
        InvalidateVisual();
        return this;
    }

    public void RemoveContent(ButtonContent content)
    {
        for (int i = 0; i < mButtonContentControllers.Count; i++)
        {
            if (mButtonContentControllers[i].Content == content)
            {
                mButtonContentControllers.RemoveAt(i);
                content.Update -= InvalidateVisual;
                return;
            }
        }
    }

    protected override void OnMouseEnter(MouseEnterEventArgs e)
    {
        CorrectColor();
    }

    protected override void OnMouseLeave(MouseLeaveEventArgs e)
    {
        CorrectColor();
    }

    protected override void OnMouseUp(MouseUpEventArgs e)
    {
        if (e.IsClick)
            Clicked?.Invoke();

        CorrectColor();
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        Pressed?.Invoke();

        CorrectColor();
    }

    public override void Render(DrawingContext context)
    {
        var rect = this.Rect();
        context.FillRectangle(Brushes.Transparent, rect);

        foreach (var content in mButtonContentControllers)
        {
            content.Content.Item.Paint(context, rect, content.Color);
        }
    }

    protected void CorrectColor()
    {
        foreach (var controller in mButtonContentControllers)
        {
            controller.Color.SetTo(DestinationColor(ref controller.Content.ColorSet), AnimationMillisec, AnimationCurve.QuadOut);
        }
        InvalidateVisual();
    }

    Color DestinationColor(ref ColorSet colorSet)
    {
        if (IsPressed)
            return colorSet.PressedColor == null ? colorSet.Color : colorSet.PressedColor.Value;

        if (IsHover)
            return colorSet.HoveredColor == null ? colorSet.Color : colorSet.HoveredColor.Value;

        return colorSet.Color;
    }

    class ButtonContentController
    {
        public ButtonContent Content;
        public AnimationColor Color;

        public ButtonContentController(ButtonContent content)
        {
            Content = content;
            Color = new() { Value = content.ColorSet.Color };
        }
    }

    readonly List<ButtonContentController> mButtonContentControllers = new();
}
