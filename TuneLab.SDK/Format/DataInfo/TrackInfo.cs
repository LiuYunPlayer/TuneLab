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
    public List<PartInfo> Parts { get; set; } = new();
    // 注：逐轨的导出开关（是否导出该轨 / 单声道还是立体声）不在此——它属 app 私有的导出配置、非 musical
    // 交换内容，与工程级的导出路径/采样率等一并留在宿主内部（见宿主的 NativeTrackInfo 子类）。
}
