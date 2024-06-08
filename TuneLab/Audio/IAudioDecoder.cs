using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Audio;

internal interface IAudioDecoder
{
    IEnumerable<string> AllDecodableFormats { get; }

    AudioInfo GetAudioInfo(string path);
    IAudioStream Decode(string path);
}
