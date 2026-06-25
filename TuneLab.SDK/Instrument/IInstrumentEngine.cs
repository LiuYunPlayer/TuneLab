using TuneLab.Foundation;

namespace TuneLab.SDK;

// 每"引擎类型"一个：加载模型、列音源目录、创建合成会话——instrument（多声部音源）专属面。
// 有状态插件（跨调用持有昂贵常驻状态，如采样库 / 模型）才有 Init/Destroy；Init 是懒调用（首次用到才调），
// 宿主也可主动预热。
//
// 与 voice 的 IVoiceEngine 同构，差异仅命名（VoiceSourceInfo → InstrumentSourceInfo，声明上下文携带
// InstrumentId），且无歌词 / 音素相关声明（instrument 无此系统）。
public interface IInstrumentEngine
{
    // 音源目录（菜单 / 选择器用，无需创建会话即可读）。契约：必须立即返回、不得阻塞——宿主与 UI 同步读取。
    // 实现应在 Init 期间扫描并缓存、get 仅返回缓存引用。
    IReadOnlyOrderedMap<string, InstrumentSourceInfo> InstrumentSourceInfos { get; }

    // 无参、失败抛异常：宿主在调用边界 catch。不传安装路径——插件 DLL 经 Assembly.Location 即可自定位包目录。
    void Init();
    void Destroy();

    // instrumentId 选定音源（InstrumentSourceInfos 的 key）；context 为该 part 的输入活视图，随会话同生共死。
    IInstrumentSession CreateSession(string instrumentId, IInstrumentContext context);

    // —— 声明（该音源暴露什么）：纯函数式获取，不依赖会话实例 ——
    // 全为当前 part 参数值的纯函数（同输入同输出、无副作用、轻量）：宿主在 part 参数 commit 时按当前值
    // 重算并 diff 到 UI，故面板 / 轨集合可随参数显隐。选哪个音源由 context.InstrumentId 给。
    // 置于引擎而非会话：声明不碰合成运行时状态，宿主据此在「建会话之前」就填好声明，无构造期时序陷阱。

    // 自动化轨配置（part 级）：连续轨与分段轨同在此 map（由 AutomationConfig.DefaultValue 是否 NaN 区分形态），
    // 按声明序呈现。轨名 / 翻译随键（PropertyKey.DisplayText，缺省回退 Id）。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IInstrumentPartPropertyContext context);

    // 合成参数回显轨声明（part 级，与 GetAutomationConfigs 同语义）：引擎产出的只读回显曲线暴露为一等只读轨，
    // 自带 Min/Max/Color。曲线数据另经 IInstrumentSession.SynthesizedParameters 按同一批 key 承载。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IInstrumentPartPropertyContext context);

    // 条件属性面板：part 级只依赖 part 自身值；note 级依赖 part + 选中 note 三态合并值。
    // 宿主在每次值 commit 时调用并 keyed-diff 到控件树。
    ObjectConfig GetPartPropertyConfig(IInstrumentPartPropertyContext context);
    ObjectConfig GetNotePropertyConfig(IInstrumentNotePropertyContext context);
}
