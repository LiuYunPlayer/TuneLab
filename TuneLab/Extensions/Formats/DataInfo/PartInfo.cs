namespace TuneLab.Extensions.Formats.DataInfo;

public abstract class PartInfo
{
    public string Name { get; set; } = string.Empty;
    public double Pos { get; set; } = 0;
    public double Dur { get; set; } = 0;
}
