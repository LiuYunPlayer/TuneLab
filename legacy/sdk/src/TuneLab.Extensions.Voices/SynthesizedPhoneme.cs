using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Voices;

public struct SynthesizedPhoneme
{
    public string Symbol;
    public double StartTime;
    public double EndTime;

    public override string ToString()
    {
        return string.Format("{{0}: [{1}, {2}]}", Symbol, StartTime, EndTime);
    }
}
