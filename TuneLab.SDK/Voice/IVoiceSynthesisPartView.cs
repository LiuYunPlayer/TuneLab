using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// voice 声明面值视图族的一员（族的说明见 IVoiceSynthesisPartPropertyContext.cs）。
// 单条 part 的只读值视图（声明面）。
public interface IVoiceSynthesisPartView
{
    // 该 part 选定声库（= IVoiceSynthesisEngine.VoiceSourceInfos 的 key）：引擎据此分流多声库。
    string VoiceId { get; }
    // part 运行期身份（= IVoiceSynthesisContext.PartId，同一个号）：插件据此查回会话为该 part 算的派生状态。
    // 声明求值不绑定具体 part 时（如无 part 的静态 schema 枚举）为空串——插件按"无派生状态"作答即可。
    string PartId { get; }
    // 当前 note 集合（只读、宿主当前序）：原始几何，未经引擎口径的去重叠 / 钳位。
    IReadOnlyList<IVoiceSynthesisNoteView> Notes { get; }
    // part 属性值快照（多选 part 时各 part 一个，三态合并归插件：context.Parts.Select(p => p.PartProperties).Merge()）。
    PropertyObject PartProperties { get; }
    // 当前**存在用户内容**的自动化轨求值器（按 key；查询轴全局秒；连续/分段/被 vibrato 投影皆算有内容，
    // 含孤儿）：只读 map，可枚举可点取。外生口径（非「已声明」）：声明集 = f(context)，按声明枚举会把
    // 引擎上一轮输出喂回声明求值；未绘制且无投影的已声明轨不在 map——其值恒为引擎自知的默认。
    IReadOnlyMap<string, IAutomationEvaluator> Automations { get; }
}
