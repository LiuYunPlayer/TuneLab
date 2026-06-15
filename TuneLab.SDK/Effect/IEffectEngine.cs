using TuneLab.Foundation;

namespace TuneLab.SDK;

// 效果器引擎：面向耗时较长的离线 effect 模型（如 SVC 换声），对一段已合成音频做整段变换。
// 一个引擎实例对应一种效果器类型；宿主为工程里每个「effect 实例 × 音频段」创建一个持久处理器驱动它。
public interface IEffectEngine
{
    // 参数面板配置：声明该效果器暴露给用户的可编辑参数（渲染为属性面板）。config 是当前参数值的纯函数——
    // 宿主在参数 commit 时按当前值重算整棵 config 并 diff 到控件树（显隐/换控件/选项随值变都是 f 的涌现）。
    // 须为纯函数（同输入同输出、无副作用、轻量）；静态面板的引擎忽略 context 返回固定 ObjectConfig 即可。
    // 命名不带 Part：effect 是单层、无 note 子级（voice 才需 GetPartPropertyConfig/GetNotePropertyConfig 区分两级）。
    ObjectConfig GetPropertyConfig(IEffectPropertyContext context);

    // 自动化轨配置：声明该效果器暴露的、可随时间变化的自动化参数（渲染为参数栏的轨）。
    // 与 GetPropertyConfig 同为当前参数值的纯函数——宿主在参数 commit 时按当前值重算轨集合并 diff 到 UI，
    // 故轨集合可随参数显隐（如某模式开关才暴露的轨）。须为纯函数（同输入同输出、无副作用、轻量）；
    // 静态轨集合的引擎忽略 context 返回固定 map 即可。
    // 孤儿数据：轨从声明消失后宿主保留其已画曲线（隐藏不删），参数回退使该轨复现即原样恢复。
    IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEffectPropertyContext context);

    // 初始化引擎（加载模型等）。无参、失败抛异常：宿主在调用边界 catch，责任归属靠捕获点判定。
    // 不传安装路径——插件 DLL 经 Assembly.Location 即可自定位包目录。
    void Init();

    // 释放引擎资源。
    void Destroy();

    // 创建一个持久处理器，绑定一条「effect 实例 × 音频段」的处理通道。宿主在该段/该 effect 存活期间
    // 持有它、按变化重复调用 Process（处理器据此跨调用复用内部中间结果）；段销毁 / effect 删除 /
    // 重分段 / 采样率变时 Dispose。
    IEffectProcessor CreateProcessor();
}
