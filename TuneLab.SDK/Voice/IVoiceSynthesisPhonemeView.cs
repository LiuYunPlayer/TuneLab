using TuneLab.Foundation;

namespace TuneLab.SDK;

// voice 声明面值视图族的一员（族的说明见 IVoiceSynthesisPartPropertyContext.cs）。
// voice 音素的只读值视图（声明面）：几何当前值 + per-phoneme 属性值快照。属性只在钉死音素上有意义。
public interface IVoiceSynthesisPhonemeView
{
    string Symbol { get; }
    double Duration { get; }              // 标称时长（秒）
    double StretchWeight { get; }
    PropertyObject Properties { get; }    // per-phoneme 属性值快照
}
