using System.Diagnostics.CodeAnalysis;
using TuneLab.Primitives.Audio;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;

namespace TuneLab.SDK.Effect;

// 一次效果器合成的输入：整段上游音频 + 该 effect 的参数快照 + 自动化取值入口。
public interface IEffectSynthesisInput
{
    // 上游音频（voice 输出或链上前一个 effect 的输出），整段提供。
    MonoAudio Audio { get; }

    // 该 effect 的参数快照（对应 PropertyConfig 声明的字段）。
    PropertyObject Properties { get; }

    // 按自动化标识取一条自动化轨的按时间取值器；不存在该轨时返回 false。
    bool TryGetAutomation(string automationId, [MaybeNullWhen(false)][NotNullWhen(true)] out IAutomationValueGetter? automation);
}
