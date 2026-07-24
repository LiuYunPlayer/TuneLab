using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Derivers;

// IAudioDerivationContext 的宿主实现：参数对话框求 config 时的当前情境（当前输入值 + 源音频元信息）。
// 对话框在用户改值时用当前值重建它、重算 config 并 diff（反应式条件字段）。
internal sealed class AudioDerivationContext : IAudioDerivationContext
{
    public PropertyObject Properties { get; init; } = PropertyObject.Empty;
}
