using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// part 属性面板 / 自动化轨声明的 config 求值上下文（注入式只读）——instrument 专属面。
// 只承载"用户改过的稀疏值"——未改过的字段不出现，其默认值由插件自己知道（默认 = 字段不存在）。
//
// 多选：PartProperties 是各选中 part 的稀疏快照列表（单选 = 1 个、无选中 = 空），宿主不替插件擅自合并。
// 与 voice 的 IVoicePartPropertyContext 同构，差异仅身份字段名（VoiceId → InstrumentId）——故属性上下文
// 不在共享中性集，instrument 持平行副本。
public interface IInstrumentPartPropertyContext
{
    // 选定音源（= 宿主侧音源 ID，IInstrumentEngine.InstrumentSourceInfos 的 key）：声明类 config 是
    // f(instrumentId, part 稀疏值) 的纯函数，引擎据此知道求值的是哪个音源。
    string InstrumentId { get; }
    // 各选中 part 的稀疏快照（多选 part 编辑时逐 part 一个；无选中 = 空列表）。
    IReadOnlyList<PropertyObject> PartProperties { get; }
}
