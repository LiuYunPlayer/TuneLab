using TuneLab.Foundation;

namespace TuneLab.SDK;

public class MidiPartInfo : PartInfo
{
    // 增益，单位 = 分贝（dB），0 = 单位增益（不增不减）。
    public double Gain { get; set; } = 0;
    public SoundSourceInfo SoundSource { get; set; } = new SoundSourceInfo();
    public List<EffectInfo> Effects { get; set; } = new();
    public List<NoteInfo> Notes { get; set; } = new();
    public Map<string, AutomationInfo> Automations { get; set; } = new();
    // 声明分段轨（除 Pitch 外、声源声明的可编辑分段曲线，即 AutomationConfig.DefaultValue 为 NaN 的轨）：按轨 id 键。
    // 孤儿数据保留隐藏，故按 map 现有内容整存（不因当前声明收缩而裁剪）。
    public Map<string, PiecewiseAutomationInfo> PiecewiseAutomations { get; set; } = new();
    // 音高偏差曲线。
    public PitchInfo Pitch { get; set; } = new();
    public List<VibratoInfo> Vibratos { get; set; } = new();
    public PropertyObject Properties { get; set; } = PropertyObject.Empty;
}
