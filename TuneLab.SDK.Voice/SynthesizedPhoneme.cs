using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Voice;

public struct SynthesizedPhoneme
{
    public string Symbol;
    public double StartTime;
    public double EndTime;

    public override string ToString()
    {
        return $"{{{Symbol}: [{StartTime}, {EndTime}]}}";
    }
}
