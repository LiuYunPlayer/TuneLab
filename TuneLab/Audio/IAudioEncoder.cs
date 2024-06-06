using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal interface IAudioEncoder
{
    void EncodeToWav(string path, float[] buffer, int samplingRate, int bitPerSample, int channelCount);
}
