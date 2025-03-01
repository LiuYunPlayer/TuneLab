using System.Collections.Generic;

namespace TuneLab.Audio;

internal interface IAudioDecoder
{
    IEnumerable<string> AllDecodableFormats { get; }

    AudioInfo GetAudioInfo(string path);
    IAudioStream Decode(string path);
}
