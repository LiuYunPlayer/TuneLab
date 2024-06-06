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
    // 传入的采样率若为0，则返回时采样率值应为音频本身的采样率；
    // 传入的采样率若为非0值，则要求解码到此采样率
    float[][] Decode(string path, ref int samplingRate);
}
