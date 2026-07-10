namespace TuneLab.Audio;

internal enum AudioExportFormat
{
    Wav,
    Mp3,
    Flac,
    Ogg,
}

// 一次编码所需的全部参数。无损格式(Wav/Flac)用 BitDepth；有损格式(Mp3/Ogg)用 Bitrate(kbps)。
// 每种格式只读取与自己相关的字段，其余忽略。
internal readonly record struct AudioEncodeSettings
{
    public required AudioExportFormat Format { get; init; }
    public int BitDepth { get; init; } // Wav: 16/24/32, Flac: 16/24
    public int Bitrate { get; init; }  // Mp3/Ogg: kbps

    public static AudioEncodeSettings Wav(int bitDepth) => new() { Format = AudioExportFormat.Wav, BitDepth = bitDepth };
}

internal static class AudioExportFormatExtensions
{
    public static string Extension(this AudioExportFormat format) => format switch
    {
        AudioExportFormat.Mp3 => ".mp3",
        AudioExportFormat.Flac => ".flac",
        AudioExportFormat.Ogg => ".ogg",
        _ => ".wav",
    };

    public static bool IsLossy(this AudioExportFormat format) => format is AudioExportFormat.Mp3 or AudioExportFormat.Ogg;
}
