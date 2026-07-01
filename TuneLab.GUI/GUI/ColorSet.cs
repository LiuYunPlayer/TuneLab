using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.GUI;

internal struct ColorSet
{
    public Color Color { get; set; }
    public Color? HoveredColor { get; set; }
    public Color? PressedColor { get; set; }
}
