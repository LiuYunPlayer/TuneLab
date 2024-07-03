using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;

namespace TuneLab.Extensions.Formats.DataInfo;

public class TrackInfo
{
    public string Name { get; set; } = string.Empty;
    public double Gain { get; set; } = 0;
    public double Pan { get; set; } = 0;
    public bool Mute { get; set; } = false;
    public bool Solo { get; set; } = false;
    public List<PartInfo> Parts { get; set; } = new();
    public Color Color { get; set; } = Color.FromArgb(255, 58, 63, 105);
}
