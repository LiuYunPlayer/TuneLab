using Avalonia.Media;
using Avalonia.Svg.Skia;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.GUI;

internal class SvgIcon(string content)
{
    public SvgImage GetImage(Color color)
    {
        return new SvgImage() { Source = SvgSource.LoadFromSvg(content.Replace("white", string.Format(rgba, color.R, color.G, color.B, (color.A / 255.0).ToString(new CultureInfo("zh-CN"))))) };
    }

    static readonly string rgba = "rgba({0},{1},{2},{3})";
}
