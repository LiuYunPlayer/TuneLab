using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media;

namespace TuneLab.GUI;

internal static class Style
{
    public static readonly Color TRANSPARENT = new(0, 0, 0, 0);
    public static readonly Color WHITE = new(255, 255, 255, 255);
    public static readonly Color LIGHT_WHITE = new(255, 181, 181, 191);
    public static readonly Color BLACK = new(255, 0, 0, 0);
    public static readonly Color TOOL_TIP_BACK = new(255, 57, 57, 64);
    public static readonly Color DARK = new(255, 18, 18, 26);
    public static readonly Color BACK = new(255, 27, 27, 36);
    public static readonly Color INTERFACE = new(255, 39, 39, 54);
    public static readonly Color LINE = new((int)(0.2 * 255), 196, 196, 196);
    public static readonly Color ITEM = new(255, 58, 63, 105);
    public static readonly Color HIGH_LIGHT = new(255, 98, 111, 252);
    public static readonly Color TEXT_NORMAL = new((int)(0.7 * 255), 255, 255, 255);
    public static readonly Color TEXT_LIGHT = new(255, 255, 255, 255);
    public static readonly Color WHITE_KEY = new(255, 39, 39, 54);
    public static readonly Color BLACK_KEY = new(255, 27, 27, 36);
    public static readonly Color BUTTON_PRIMARY = new(255, 96, 96, 192);
    public static readonly Color BUTTON_NORMAL = new(255, 58, 63, 105);
    public static readonly Color BUTTON_PRIMARY_HOVER = new(255, 127, 127, 255);
    public static readonly Color BUTTON_NORMAL_HOVER = new(255, 85, 92, 153);

    public static readonly Color AMP_NORMAL = new(255, 102, 255, 51);
    public static readonly Color AMP_DELAY = new((int)(0.5 * 255), 102, 255, 51);

    public static readonly List<Color> TRACK_COLORS =
    [
        Color.FromRgb(58, 63, 105),      
        Color.FromRgb(4, 177, 42),  
        Color.FromRgb(146, 22, 46),  
        Color.FromRgb(253, 23, 100),
        Color.FromRgb(208, 95, 138),   
        Color.FromRgb(138, 36, 179),  
        Color.FromRgb(4, 151, 244),  
        Color.FromRgb(1, 152, 135),  
        Color.FromRgb(1, 90, 44), 
        Color.FromRgb(155, 197, 27),
        Color.FromRgb(213, 197, 39), 
        Color.FromRgb(254, 90, 29),    
        Color.FromRgb(133, 86, 70),   
        Color.FromRgb(188, 72, 223),  
        Color.FromRgb(128, 126, 127)
    ];
}
