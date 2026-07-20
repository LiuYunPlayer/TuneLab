namespace TuneLab.SDK;

public class TrackInfo
{
    public string Name { get; set; } = string.Empty;
    // 增益，单位 = 分贝（dB），0 = 单位增益。
    public double Gain { get; set; } = 0;
    // 声像，范围 [-1, 1]：-1 = 全左、0 = 居中、+1 = 全右（左增益 = 1 - Pan、右增益 = 1 + Pan）。
    public double Pan { get; set; } = 0;
    public bool Mute { get; set; } = false;
    public bool Solo { get; set; } = false;
    public bool AsRefer { get; set; } = true;
    public string Color { get; set; } = string.Empty;
    public bool ExportEnabled { get; set; } = false;
    public int ExportChannels { get; set; } = 1; // 1 = mono, 2 = stereo
    public List<PartInfo> Parts { get; set; } = new();
}
