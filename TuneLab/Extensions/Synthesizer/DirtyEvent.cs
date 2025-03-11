using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Synthesizer;

internal class DirtyEvent
{
    public bool Handled { get; set; }

    public void Accept()
    {
        Handled = true;
    }
}
