using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// instrument 声明面值视图族的一员（族的说明见 IInstrumentSynthesisPartPropertyContext.cs）。
// 单条 part 的只读值视图（声明面）。
public interface IInstrumentSynthesisPartView
{
    // 该 part 选定音源（= IInstrumentSynthesisEngine.InstrumentSourceInfos 的 key）：引擎据此分流多音源。
    string InstrumentId { get; }
    // part 运行期身份（= IInstrumentSynthesisContext.PartId）；声明求值不绑定具体 part 时为空串。
    string PartId { get; }
    // 当前 note 集合（只读、宿主当前序）：原始几何，instrument 不去重叠。
    IReadOnlyList<IInstrumentSynthesisNoteView> Notes { get; }
    PropertyObject PartProperties { get; }
    // 当前**存在用户内容**的自动化轨求值器（按 key；查询轴全局秒；口径与理由同 IVoiceSynthesisPartView.Automations：
    // 外生输入、非「已声明」——未绘制且无 vibrato 投影的已声明轨不在 map，其值恒为引擎自知的默认）。
    IReadOnlyMap<string, IAutomationEvaluator> Automations { get; }
}
