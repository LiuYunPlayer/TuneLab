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
        // alpha 恒用 InvariantCulture 出句点小数点：rgba(...) 是机器可读串，逗号区域的 CurrentCulture 会出 "0,5"
        // 多一个逗号 → 分量数错乱 → 部分地区 svg 图标不显示（1.0 曾栽此坑、当年用 zh-CN 碰巧句点绕过，此为正解）。
        return new SvgImage() { Source = SvgSource.LoadFromSvg(content.Replace("white", string.Format(rgba, color.R, color.G, color.B, (color.A / 255.0).ToString(CultureInfo.InvariantCulture)))) };
    }

    static readonly string rgba = "rgba({0},{1},{2},{3})";
}
