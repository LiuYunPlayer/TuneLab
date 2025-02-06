using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal interface IAudioSampleProvider
{
    void Read(float[] buffer, int offset, int count);
}
