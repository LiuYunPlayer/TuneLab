using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.Utils;

namespace TuneLab.UI;

internal class ParameterTitleBar : MovableComponent
{
    public new event Action<double>? Moved;

    public ParameterTitleBar()
    {
        base.Moved.Subscribe(p => Moved?.Invoke(p.Y));
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new Color(255, 51, 51, 64).ToBrush(), this.Rect());
    }
}
