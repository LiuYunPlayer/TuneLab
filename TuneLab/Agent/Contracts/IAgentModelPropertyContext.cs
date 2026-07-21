using TuneLab.Foundation;

namespace TuneLab.Agent;

// 模型引擎参数面板 config 求值上下文（注入式只读），与 IEffectSynthesisPropertyContext 同形。
// 承载用户在设置界面"已填过的稀疏值"（如已选的端点类型），引擎据此返回当前应呈现的 ObjectConfig
// （例如选了不同 provider 时暴露不同字段）。读不到的 key 由引擎按自身默认 fallback。
// 冻结接口纯加性扩展：以后加只读属性不破坏既有插件。
public interface IAgentModelPropertyContext
{
    PropertyObject Properties { get; }
}
