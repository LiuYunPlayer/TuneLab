using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// part 音源的宿主门面：持久化身份（Kind/Type/ID，undo 面）+ 声明视图。
// 「声源」泛化为 voice（去重叠单声部歌声）或 instrument（重叠多声部音源）两类——种类由 Kind 判别。
// 声明（默认歌词/自动化轨/属性面板）在宿主侧物化缓存，按 Kind 委派给对应引擎管理器（Voices / Instruments）。
//
// 单一稳定数据对象（身份不变、Modified 事件不断线）：换种类只改 Kind/Type/ID（同 SetInfo），不替换对象——
// 故外部对 part.SoundSource.Modified 的订阅永不悬挂。声明 context 是宿主 PartPropertyContext/NotePropertyContext
//（一套实现同时满足 voice / instrument 两域的 SDK 声明 context），实现按 Kind 上行委派对应管理器。
internal interface ISoundSource : IDataObject<SoundSourceInfo>
{
    SourceKind Kind { get; }
    string Type { get; }
    string ID { get; }
    string Name { get; }
    // 新 note 的默认歌词：voice 取会话级 DefaultLyric；instrument 无歌词系统，恒 "a"（note.Lyric 字段对其无意义但需有值）。
    string DefaultLyric { get; }
    // 连续轨与分段轨同在此 map（kind 由 AutomationConfig.IsPiecewise 现解析）。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> AutomationConfigs { get; }
    // 合成参数回显轨声明（只读、独立于可编辑轨集合）：曲线数据经 IMidiPart.SynthesizedParameters 按同一批 key 承载。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> SynthesizedParameterConfigs { get; }
    ObjectConfig GetPartPropertyConfig(PartPropertyContext context);
    ObjectConfig GetNotePropertyConfig(NotePropertyContext context);
}
