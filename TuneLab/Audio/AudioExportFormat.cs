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
    // 持久化 / 对外的格式 id（工程文件 exportConfig.format、导出侧栏下拉、脚本 project.exportFormat 共用一份）。
    // 与枚举同处一文件：加一种格式时这张表和 Extension() 在同一屏内，漏改无处可藏。
    public static readonly string[] AllIds = ["wav", "mp3", "flac", "ogg"];

    public static string Id(this AudioExportFormat format) => format switch
    {
        AudioExportFormat.Mp3 => "mp3",
        AudioExportFormat.Flac => "flac",
        AudioExportFormat.Ogg => "ogg",
        _ => "wav",
    };

    // 严格解析（未知 id 返回 false）：给需要报错而非静默回退的调用方（脚本面写 exportFormat）。
    public static bool TryParseId(string? id, out AudioExportFormat format)
    {
        switch (id)
        {
            case "mp3": format = AudioExportFormat.Mp3; return true;
            case "flac": format = AudioExportFormat.Flac; return true;
            case "ogg": format = AudioExportFormat.Ogg; return true;
            case "wav": format = AudioExportFormat.Wav; return true;
            default: format = AudioExportFormat.Wav; return false;
        }
    }

    // 宽容解析（未知 id 回退 wav）：给读工程文件 / UI 下拉那类"坏值不该拦住用户"的调用方。
    public static AudioExportFormat ParseId(string? id) => TryParseId(id, out var format) ? format : AudioExportFormat.Wav;

    public static string Extension(this AudioExportFormat format) => format switch
    {
        AudioExportFormat.Mp3 => ".mp3",
        AudioExportFormat.Flac => ".flac",
        AudioExportFormat.Ogg => ".ogg",
        _ => ".wav",
    };

    public static bool IsLossy(this AudioExportFormat format) => format is AudioExportFormat.Mp3 or AudioExportFormat.Ogg;
}
