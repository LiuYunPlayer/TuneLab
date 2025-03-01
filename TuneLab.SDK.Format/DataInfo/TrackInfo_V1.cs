namespace TuneLab.SDK.Format.DataInfo;

public class TrackInfo_V1
{
    public string Name { get; set; } = string.Empty;
    public double Gain { get; set; } = 0;
    public double Pan { get; set; } = 0;
    public bool Mute { get; set; } = false;
    public bool Solo { get; set; } = false;
    public bool AsRefer { get; set; } = true;
    public string Color { get; set; } = string.Empty;
    public List<PartInfo_V1> Parts { get; set; } = [];
}
