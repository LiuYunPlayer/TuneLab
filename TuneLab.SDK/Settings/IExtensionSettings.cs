using TuneLab.Foundation;

namespace TuneLab.SDK;

// 扩展能力级持久设置（opt-in）：任何扩展能力的实现类（voice/effect 引擎等）按需"再实现"它，声明一组随宿主、
// 跨工程持久化、与运行时实例/段参数无关的配置——典型如 API key、模型路径、设备选择。宿主在设置窗口渲染面板、
// 按能力落盘、运行时回喂。区别于 IEffectSynthesisEngine.GetPropertyConfig 等"随工程序列化的实例/段级属性"。
//
// 命名取 extension（= 宿主术语里"per 能力实现者单位"；安装包是 ExtensionPackage、可含多个 extension），
// 不锚死 "engine"——便于将来非 engine 的顶级能力类型也接入设置。粒度是【per extension】（如每个 voice/effect
// 引擎各一份），不是 per ExtensionPackage：一个包里两个引擎各自实现、各存一份。
//
// 探测式接入：宿主对每个已注册能力做 `x is IExtensionSettings` 判定，实现了才显示其设置面板——故无设置的
// 能力不必实现，零负担、对既有能力接口零破坏。
public interface IExtensionSettings
{
    // 声明设置 schema（复用通用控件配置词汇 ObjectConfig）。宿主据此渲染面板。
    // 密钥类字段用 TextBoxConfig { IsPassword = true } 标出——宿主据此掩码显示并加密落盘。
    // context 携当前已填的设置值——是当前值的**纯函数**：宿主在用户改值后按当前值重算整棵 config 并 diff 到控件树，
    // 故可据已填值条件显隐字段（如选了某模式才暴露的字段）。静态面板忽略 context 返回固定 config 即可。
    // 与 GetPropertyConfig 同约定：须为纯函数（同输入同输出、无副作用、轻量），且**必须在 Init 之前可调**
    //（"先填模型路径，Init 才加载得了模型"——故 schema 不能依赖 Init 后的状态）。
    // DisplayText 由插件自译（V1.I18N 范式），宿主不参与查表。
    ObjectConfig GetSettingsConfig(IExtensionSettingsContext context);

    // 宿主把持久化的设置值回喂给实现者：加载完成后灌一次（早于任何 Init/会话），用户在设置窗口保存后再灌一次。
    // 实现者自存这些值、自行用于 Init/CreateSession/CreateProcessor；设置变更对已在运行实例的影响（是否需重建
    // 会话/处理器）由实现者自理。读不到的字段按自身默认 fallback。
    void ApplySettings(PropertyObject settings);
}
