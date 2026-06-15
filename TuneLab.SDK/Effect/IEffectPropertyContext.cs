using TuneLab.Foundation;

namespace TuneLab.SDK;

// 效果器条件属性面板 config 求值上下文（注入式只读）：引擎的 GetPropertyConfig 据此返回当前应呈现的 ObjectConfig。
// effect 单层——只承载该 effect 自身「用户改过的稀疏值」（未改过的字段不出现，其默认值由引擎自己知道）。
// 读不到的 key 取到 Invalid，引擎按自身默认 fallback。
// 冻结接口纯加性扩展：以后加只读属性（如 project 级 tempo / 采样率）不破坏既有插件。
public interface IEffectPropertyContext
{
    PropertyObject Properties { get; }
}
