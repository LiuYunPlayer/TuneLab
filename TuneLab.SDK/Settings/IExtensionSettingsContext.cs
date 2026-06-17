using TuneLab.Foundation;

namespace TuneLab.SDK;

// IExtensionSettings.GetSettingsConfig 的求值上下文（注入式只读），与 IEffectPropertyContext /
// IAgentModelPropertyContext 同形。承载用户在设置面板已填的当前值——扩展据此返回当前应呈现的 ObjectConfig
//（条件显隐：如选了某模式才暴露的字段）。读不到的 key 由扩展按自身默认 fallback。
// 冻结接口纯加性扩展：以后加只读属性不破坏既有扩展。
public interface IExtensionSettingsContext
{
    PropertyObject Settings { get; }
}
