using System;

namespace TuneLab.SDK;

// 音频段握柄：宿主实现、经 IVoiceSynthesisContext.CreateAudioSegment 分配，插件持有并写入。
// 它是音频产物的承载单元，也是下游 effect 链的失效/重渲染单元——一个段 Commit 即作为整体送
// effect 重过（effect 缓存按握柄身份键：段重 Commit → 该段链重跑；段销毁 → 丢该段缓存）。
// 段的起始与长度在创建时声明（宿主据此分配缓冲，就地写入故渐进合成不累积重拷）；
// 几何需变：**语义整改换新 → 删旧段（Dispose）建新段（下游身份随之重建）**；
// **内容延续的扩展/裁剪 → Resize（身份保持，下游缓存存活）**——如 inpainting 引擎在内容前/后新增。
// 时间对齐协议：全局 0 时刻 = 采样点 0；缓冲按插件 native 采样率从段起始铺。
// 线程：写入 / 提交 / 释放全在数据线程（worker 渲染完，在 marshal 回数据线程的续延里写）。
public interface IAudioSegment : IDisposable   // Dispose() = 删除该段（语义整改换新时重建）
{
    // 段内 [offset, offset+samples.Length) 就地写入（offset = 段内相对采样位置，0 = 段首）；
    // 宿主把 samples 拷进自有缓冲的该区间——span 借用语义，返回后插件可随意复用 / 池化该缓冲。
    // 越界（超出创建声明的 sampleCount）非法。可多次写、可覆盖重写、可在 Commit 后再写（重回未提交态）。
    // 写入区间即"该子区间已更新"信号（将来段内局部 effect 重渲据此；当前整段失效暂不消费区间）。
    void Write(int offset, ReadOnlySpan<float> samples);

    // 标该段音频已固定——送 effect 的唯一闸门。Commit 前的写入只供进度 / 波形展示，冻结数据（Commit）
    // 才进 effect，故合成爆发期不会拖着昂贵 effect 频繁重合成（闸门在协议层，非宿主防抖）。
    void Commit();

    // 就地改几何、**身份不变**（下游 effect 链节点与缓存存活）：交集内容按全局采样位置对齐保留
    //（内容钉在绝对轴，前向/后向扩展对称；缩短则裁弃出界内容），新增区域清零。
    // 几何对称差随下次 Commit 如实入账给下游（裁剪区/新增区都是下游的上下文变更，重算范围由下游自决）。
    // 段回到未提交态——写完新增区域后重 Commit 收口；未重 Commit 前下游持续消费上一版已提交内容
    //（陈旧但自洽）。Resize 与后续写入/Commit 不要求同一续延（可跨异步渲染）。
    // 适用：内容延续的扩展/裁剪（如 inpainting 引擎在内容首/尾新增 note）；语义整改换新仍走 Dispose+Create。
    // 仅数据线程调用；超出宿主当前存储实现上限时抛出。
    void Resize(long sampleOffset, int sampleCount);
}
