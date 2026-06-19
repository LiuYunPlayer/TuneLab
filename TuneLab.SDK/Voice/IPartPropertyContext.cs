using TuneLab.Foundation;

namespace TuneLab.SDK;

// 属性面板 config 求值上下文（注入式只读）：插件据此返回当前应呈现的 ObjectConfig。
// 只承载"用户改过的稀疏值"——未改过的字段不出现，其默认值由插件自己知道（默认 = 字段不存在）。
// 读不到的 key 取到 Invalid，插件按自身默认 fallback。冻结接口纯加性扩展（以后加只读属性不破坏旧插件）。
//
// 分两级、单向依赖：
//   part 级（IPartPropertyContext）—— 输入只有 part 自身稀疏值，即面板内部联动（某控件影响同面板另一控件）；
//     part 面板/自动化轨声明据此求值，不依赖 note 选择。
//   note 级（INotePropertyContext）—— part 稀疏值 + 选中 note 的三态合并稀疏值（同 key 全等给值、不等给 Multiple）。
// 上游 commit 沿链触发下游重算（part 改 → 重算 part + note），下游不影响上游 → 无环、方向确定。
public interface IPartPropertyContext
{
    // 选定声库（= 宿主 Voice.ID，IVoiceEngine.VoiceSourceInfos 的 key）：声明类 config 是
    // f(voiceId, part 稀疏值) 的纯函数，voiceId 随 context 一并注入，引擎据此知道求值的是哪个模型。
    // 这使 voice 的 context 必然带身份、与 effect 的 IEffectPropertyContext（单类型、无此对等物）永久分叉。
    string VoiceId { get; }
    PropertyObject PartProperties { get; }
}

// note 级 = part 级 + note 合并值（note config 可同时依赖 part 设置与该 note 自身值）。
public interface INotePropertyContext : IPartPropertyContext
{
    PropertyObject NoteProperties { get; }
}
