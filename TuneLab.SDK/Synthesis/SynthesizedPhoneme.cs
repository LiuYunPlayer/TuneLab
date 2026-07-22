namespace TuneLab.SDK;

// 音素描述符（方向无关，输入 / 输出共用一个类型）：只报「标称时长 + 弹性权重」，**不报绝对位置、不报前后归属**。
// 引导 / 主体归属做成结构化双列表（LeadingPhonemes / BodyPhonemes，见 SynthesizedSyllable），不落每音素标志——
// 消除「交替 IsLead」这类可表示非法态。进（用户钉死约束，挂 IVoiceSynthesisNote 的两列表）与出（引擎合成产物，
// IVoiceSynthesisSession.SynthesizedPhonemes 的 SynthesizedSyllable 两列表）同形；持久层 PhonemeInfo 与本类型解耦。
//
// 时长模型：引擎只报标称时长，定位 / 跨 note 去重叠压缩 / melisma 铺设全由宿主按同一时长模型派生
// （核起点=音符头、前置往左累积、核填到满末）。引擎报已压缩的绝对位置会让宿主布局误判（相接判据失真），
// 故只报自然时长、宿主独占布局。引擎自己的音频内部如何摆放是引擎的事，与此显示契约解耦。
//
// 把标称时长解析为真实时序的布局算法在 SDK（PhonemeLayout.Resolve，纯函数）：想与宿主显示完全一致
// （WYSIWYG）的引擎调它即可——冻结的只是 I/O 形状，压缩内部逻辑仍可宿主侧演进，引擎运行时绑定宿主的这一份故永不漂移。
// 想自管音频摆放（交叉淡入等）的引擎可不调，仍按本描述符自由放置、错位非致命。
//
// 粒度为整 note：IVoiceSynthesisNote.Phonemes 列表非空 = 全部音素用户钉死（引擎遵守约束）；为空 = 引擎从 Lyric 做 G2P + 全自由定时。
//
// 形态 = readonly struct + init 属性（合成域值 DTO 的房规默认）：值语义 + 不可变 + 对象初始化器 ergonomics
// 与裸公开字段等同，但成员经属性访问器（get_X）稳定 ABI 面暴露——冻结后仍可把某成员的 backing 演进为
// 带校验 / 计算的属性而不破 ABI（裸字段则永久锁死）。构造点一律 new SynthesizedPhoneme { Symbol = …, … }。
//
// 注意：`default(SynthesizedPhoneme)` / `new SynthesizedPhoneme[n]` 零填内存、不经构造器，Symbol 会是 null
// （违背非空声明）——**default 非有效实例，务必经初始化器/赋值构造**。本类型是列表里的叶描述符、非 map 值型，
// 无 TryGetValue-miss 这类外部可达的 default 路径，故不为此加防御 getter；若将来 default 变得外部可达，按上段
// 的 backing 演进路给 Symbol 加 `?? ""` 兜底即可，零 ABI 代价。
public readonly struct SynthesizedPhoneme
{
    public string Symbol { get; init; }

    // 标称时长（秒）：辅音(StretchWeight=0)为其固定长；核 / 元音(StretchWeight>0)为其原长——布局按缩放比
    // len/d = r^w 分配（r 由可用空间守恒定）：单核时原长被抵消（恒填满核空间）；多核时原长定彼此基准比例。
    public double Duration { get; init; }

    // 伸缩权重：0 = 刚性辅音（长度固定、不参与伸缩）；>0 = 可伸核 / 元音，缩放比 len/d = r^w——
    // w 即「对数缩放敏感度」：同权重 ⇒ 同缩放比（等比保形），w 越大伸缩越剧烈，w=2 是 w=1 的平方。
    // 用户锁定音素时随时长一并固定为用户数据。全 w=0（含未设、默认零）时宿主退化为按原长整体等比缩放，无除零。
    public double StretchWeight { get; init; }

    public override string ToString()
    {
        return $"{{{Symbol}: {Duration}s w={StretchWeight}}}";
    }
}
