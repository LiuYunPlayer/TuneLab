namespace TuneLab.SDK;

// 派生音频 part（分离 stem / 变声等生成型音频，或按静音段切分产的音频片段）：与 midi part 平级。
// 缓后：v1 不产音频，本类型先占位、无负载字段。音频交付机制（倾向宿主给路径）待实现音频输出时定死，
// 届时按加性补负载字段（旧插件不填即无音频）。见 docs/deriver-sdk-design.md §8。
public sealed class DerivedAudioPart : DerivedPart
{
}
