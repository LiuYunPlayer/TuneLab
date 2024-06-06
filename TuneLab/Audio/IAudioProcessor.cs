using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal interface IAudioProcessor
{
    void ProcessBlock(float[] buffer, int offset, int position, int count);
}
