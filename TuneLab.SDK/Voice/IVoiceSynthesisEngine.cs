using TuneLab.Foundation;

namespace TuneLab.SDK;

// 每"引擎类型"一个：加载模型、列声库目录、创建合成会话。
// 有状态插件（跨调用持有昂贵常驻状态，如模型）才有 Init/Destroy；
// Init 是懒调用（首次用到才调），宿主也可主动预热。
//
// 加性约定（插件实现面）：将来在本面新增成员一律用默认接口方法（DIM）给兜底体，使增补不破已装插件。
public interface IVoiceSynthesisEngine
{
    // 声库目录（菜单/选择器用，无需创建会话即可读）。属性名与值类型 VoiceSourceInfo 对齐。
    // 契约：必须立即返回、不得阻塞——宿主与 UI 同步读取、无异步等待。实现应在 Init 期间扫描并
    // 缓存声库、get 仅返回缓存引用；惰性加载（首次 get 才扫盘）者自负阻塞 UI 之责。
    IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos { get; }

    // 无参、失败抛异常：宿主在调用边界 catch，责任归属靠捕获点判定（从插件调用边界出来的就是插件侧责任）。
    // 不传安装路径——插件 DLL 经 Assembly.Location 即可自定位包目录。
    void Init();
    void Destroy();

    // context 为该 part 的输入活视图（含 VoiceId），随会话同生共死；voiceId 已并入 context、不再单列。
    IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context);

    // —— 声明（该声源暴露什么）：纯函数式获取，不依赖会话实例 ——
    // 全为当前 part 真值的纯函数（同输入同输出、无副作用、轻量）：宿主在值 commit 时按当前值重算并 diff 到 UI，
    // 故面板/轨集合可随参数显隐（如某模式开关才暴露的轨）。声明面收**调用级只读活视图**（IVoiceSynthesisPartView，voice 专属、
    // 与 instrument 持平行副本）——引擎可读 part 当前真值（含非属性字段：note 时间/音高/歌词、已声明自动化曲线、
    // note 集合）决定 schema。选哪个声库由 context.Parts[i].VoiceId 给。仅数据线程同步调用、只读不订阅。
    // 静态声明的插件忽略 context 返回固定值即可。

    // 自动化轨配置（part 级）：连续轨与分段轨同在此 map（由 AutomationConfig.DefaultValue 是否 NaN 区分形态），
    // 按声明序呈现。轨名/翻译随键（PropertyKey.DisplayText，缺省回退 Id）。
    // 孤儿数据：轨从声明消失后宿主保留其已画曲线（隐藏不删），参数回退使该轨复现即原样恢复。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context);

    // 合成参数回显轨声明（part 级，与 GetAutomationConfigs 同语义）：引擎产出的只读回显曲线（如 energy）
    // 暴露为一等只读轨，轨名随键、自带 Min/Max/Color。回显是分段形（DefaultValue 置 NaN，无基线、段间断开）。
    // 曲线数据另经 IVoiceSynthesisSession.SynthesizedParameters 按同一批 key 承载。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context);

    // 条件属性面板：part（会话主体）级只依赖 part 自身值；note 级依赖 part + 选中 note 三态合并值。
    // 宿主在每次值 commit 时调用并 keyed-diff 到控件树。
    ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context);
    ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context);

    // per-phoneme 自定义属性声明（required；逐音素求值）：复用 note 声明上下文（`IVoiceSynthesisNotePropertyContext`——
    // 其 NoteView 现带 `Phonemes`），返回与"选中各 note 的音素**扁平展开**（context.Notes 顺序 × 各 note 的 Phonemes 顺序）"
    // **索引对齐**的 config 列表（list[k] = 第 k 个扁平音素的 schema）。故 schema 可依音素在 note 内的位置 / 邻居 / note 信息
    // 条件化（如首辅音 vs 核），且天然支持多选 note。**返回空列表 = 所有音素均无属性**；否则长度须 = 扁平音素总数。
    // 值回灌到钉死音素（合成时经 VoiceSynthesisPhonemeSnapshot.Properties 读取）。属性只存在于钉死音素上（用户数据）。
    IReadOnlyList<ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context);
}
