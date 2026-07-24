using TuneLab.Foundation;

namespace TuneLab.SDK;

// GetPropertyConfig 的上下文（宿主实现、插件只读）：参数对话框求 config 时的当前情境。
// 让 config 成为「当前情境的纯函数」——用户改任一输入即重算 schema 并 diff 到控件树（条件字段：
// 选了某模式才露出对应参数），与 voice/effect 的反应式 config 同范式。
//
// 独立成接口而非裸传 PropertyObject：为将来加性扩展留面（如按源音频特征调整参数默认/量程、暴露选区范围等），
// 加成员纯加性、不破已装插件。deriver 一次性、无常驻 session，故本上下文仅服务对话框求值期。
public interface IAudioDerivationContext
{
    // 对话框当前已填的参数值（稀疏：只含用户改过的键，缺键取 config 默认）。
    PropertyObject Properties { get; }
    // 将来可加性扩展（如源音频元信息 SampleRate/SampleCount 供 config 依据音频定默认）；本接口宿主实现、加成员不破插件。
    // 暴露原语（精确样本数）而非有损派生量（秒），届时再定。
}
