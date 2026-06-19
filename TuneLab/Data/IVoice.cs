using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// part 声源的宿主门面：持久化身份（Type/ID，undo 面）+ 声明视图。
// 声明（默认歌词/自动化轨/属性面板）挂在合成会话上（厚插件原则），这里转发 part 当前会话；
// 会话未建（part 未激活/引擎不可用）时回退默认声明。
internal interface IVoice : IDataObject<VoiceInfo>
{
    string Type { get; }
    string ID { get; }
    string Name { get; }
    string DefaultLyric { get; }
    // 连续轨与分段轨同在此 map（kind 由 AutomationConfig.IsPiecewise 现解析）。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> AutomationConfigs { get; }
    // 合成参数回显轨声明（只读、独立于可编辑轨集合）：曲线数据经 IMidiPart.SynthesizedParameters 按同一批 key 承载。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> SynthesizedParameterConfigs { get; }
    ObjectConfig GetPartPropertyConfig(IPartPropertyContext context);
    ObjectConfig GetNotePropertyConfig(INotePropertyContext context);
}
