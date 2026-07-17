using TuneLab.Foundation;

namespace TuneLab.SDK;

// 合成管线中的 effect 级引擎（Synthesis 为管线域标记，解析作 [Effect][SynthesisEngine]——effect 对
// 已合成音频做离线变换，自身不合成；与 voice/instrument 同族共用 SynthesisStatusSegment 声称词汇、
// SynthesizedParameter 回显、合成状态带与调度闸门）。
// 面向耗时较长的离线 effect 模型（如 SVC 换声），对一段已合成音频做整段变换。
// 一个引擎实例对应一种效果器类型；宿主为工程里每个「effect 实例 × 音频段」创建一个持久处理器驱动它。
public interface IEffectSynthesisEngine
{
    // 参数面板配置：声明该效果器暴露给用户的可编辑参数（渲染为属性面板）。config 是当前参数值的纯函数——
    // 宿主在参数 commit 时按当前值重算整棵 config 并 diff 到控件树（显隐/换控件/选项随值变都是 f 的涌现）。
    // 须为纯函数（同输入同输出、无副作用、轻量）；静态面板的引擎忽略 context 返回固定 ObjectConfig 即可。
    // 命名不带 Part：effect 是单层、无 note 子级（voice 才需 GetPartPropertyConfig/GetNotePropertyConfig 区分两级）。
    ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context);

    // 自动化轨配置：声明该效果器暴露的、可随时间变化的自动化参数（渲染为参数栏的轨）。
    // 与 GetPropertyConfig 同为当前参数值的纯函数——宿主在参数 commit 时按当前值重算轨集合并 diff 到 UI，
    // 故轨集合可随参数显隐（如某模式开关才暴露的轨）。须为纯函数（同输入同输出、无副作用、轻量）；
    // 静态轨集合的引擎忽略 context 返回固定 map 即可。
    // 孤儿数据：轨从声明消失后宿主保留其已画曲线（隐藏不删），参数回退使该轨复现即原样恢复。
    // 连续轨与分段轨同在此 map（由 AutomationConfig.DefaultValue 是否 NaN 区分形态），按声明序呈现。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context);

    // 合成参数回显轨配置（只读、独立于可编辑自动化轨）：与 GetAutomationConfigs 同为当前参数值的纯函数。
    // 引擎处理产出的只读回显曲线（如 loudness）暴露为一等只读轨，轨名随键、自带 Min/Max/Color——宿主据此
    // 显隐、用各自色绘制，不可编辑。回显是分段形（DefaultValue 置 NaN，无基线、段间断开）。
    // 须为纯函数（同输入同输出、无副作用、轻量）；无回显的引擎返回空 map 即可。
    // 曲线数据另经 IEffectSynthesisSession.SynthesizedParameters 按同一批 key 承载。
    IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context);

    // 初始化引擎（加载模型等）。无参、失败抛异常：宿主在调用边界 catch，责任归属靠捕获点判定。
    // 不传安装路径——插件 DLL 经 Assembly.Location 即可自定位包目录。
    void Init();

    // 释放引擎资源。
    void Destroy();

    // 创建一个持久厚处理器，绑定一条「effect 实例 × 一个上游音频段」的处理通道（context 由宿主实现、
    // 暴露本段输入 + 该 effect 参数/自动化 + 产出口）。失效判定与调度归宿主（处理器零上报义务，
    // 被调到 Process 就按当前真相干活——电平语义，见 IEffectSynthesisSession）；
    // 段销毁 / effect 删除 / 重分段 / 采样率变时宿主 Dispose 处理器。
    IEffectSynthesisSession CreateSession(IEffectSynthesisContext context);
}
