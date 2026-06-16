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
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    IReadOnlyOrderedMap<string, PiecewiseAutomationConfig> PiecewiseAutomationConfigs { get; }
    ObjectConfig GetPartPropertyConfig(IPartPropertyContext context);
    ObjectConfig GetNotePropertyConfig(INotePropertyContext context);
}
