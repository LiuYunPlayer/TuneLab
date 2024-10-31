using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Format.DataInfo;

public class PhonemeInfo_V1
{
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public string Symbol { get; set; } = string.Empty;
}
