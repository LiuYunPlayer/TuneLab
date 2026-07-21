namespace TuneLab.SDK;

// 声库选择器的呈现布局：一棵有序树，节点 = 声库引用 | 子组，可在同层任意交织。
// 与身份权威 IVoiceSynthesisEngine.VoiceSourceInfos（id→info 扁平 map）平行——布局只管「怎么摆」，
// 身份/查找/工程序列化引用仍走那张 map；叶子只引用 id，显示名由宿主从 map 取（VoiceSourceInfo.Name），不复制。
// 未被布局任何一层引用到的 id：宿主兜底在顶层按 map 序补出（故空布局 = 全部平铺 = 无布局时的旧行为）。
//
// 封闭层级：本树只有 Voice / Group 两态，宿主据此穷举匹配。base 构造 private protected——
// 外部只能用下面两个 sealed 子类（或其等价工厂）造节点，不能派生第三态，保证匹配穷尽。
public abstract class VoiceSourceLayoutItem
{
    private protected VoiceSourceLayoutItem() { }

    // 便捷工厂：与对应子类的 required-init 初始化器等价，二选一（列表拼装时更短）。
    public static VoiceSourceLayoutItem Voice(string voiceId) => new VoiceSourceLayoutVoice { VoiceId = voiceId };
    public static VoiceSourceLayoutItem Group(string name, IReadOnlyList<VoiceSourceLayoutItem> items) => new VoiceSourceLayoutGroup { Name = name, Items = items };
}

// 叶子：引用一个声库 id（须存在于 VoiceSourceInfos 的键集；宿主对悬垂 id 容错跳过、不报错）。
public sealed class VoiceSourceLayoutVoice : VoiceSourceLayoutItem
{
    public required string VoiceId { get; init; }
}

// 子组：一个已本地化的组名（由引擎按当前语言产出，同 VoiceSourceInfo.Name）+ 有序子节点，可再嵌套。
// 空 Items 合法：表示「组存在但暂无子项」，宿主渲染为空子菜单、不可选（同 ComboBoxItem 空分组语义）。
public sealed class VoiceSourceLayoutGroup : VoiceSourceLayoutItem
{
    public required string Name { get; init; }
    public IReadOnlyList<VoiceSourceLayoutItem> Items { get; init; } = [];
}
