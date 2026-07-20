namespace TuneLab.SDK;

public class AudioPartInfo : PartInfo
{
    // 音频文件路径：绝对路径，或相对工程文件所在目录的相对路径（以 ".." 起头、可多层上级，按工程目录解析）。
    public string Path { get; set; } = string.Empty;
}
