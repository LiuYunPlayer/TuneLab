using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Data;

namespace TuneLab.Audio;

internal static class AudioUtils
{
    public readonly static string[] AllSupportedFormats = ["*.wav", "*.mp3", "*.aiff", "*.aac", "*.wma", "*.mp4"];

    public static bool TryGetAudioInfo(string path, [NotNullWhen(true)] out AudioInfo audioInfo)
    {
        audioInfo = new AudioInfo();
        try
        {
            using (var reader = new AudioFileReader(path))
            {
                audioInfo.duration = reader.TotalTime.TotalSeconds;
                return true;
            }
        }
        catch
        {
            return false; 
        }
    }
}
