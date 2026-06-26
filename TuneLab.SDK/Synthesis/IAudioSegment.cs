using System;

namespace TuneLab.SDK;

// 音频段握柄：宿主实现、经 IVoiceSynthesisContext.CreateAudioSegment 分配，插件持有并写入。
// 它是音频产物的承载单元，也是下游 effect 链的失效/重渲染单元——一个段 Commit 即作为整体送
// effect 重过（effect 缓存按握柄身份键：段重 Commit → 该段链重跑；段销毁 → 丢该段缓存）。
// 段的起始与长度在创建时固定（宿主据此一次性分配缓冲，就地写入故渐进合成不累积重拷）；
// 位置 / 长度需变 → 删旧段（Dispose）建新段。
// 时间对齐协议：全局 0 时刻 = 采样点 0；缓冲按插件 native 采样率从段起始铺。
// 线程：写入 / 提交 / 释放全在数据线程（worker 渲染完，在 marshal 回数据线程的续延里写）。
public interface IAudioSegment : IDisposable   // Dispose() = 删除该段（重分片 / 改长度或位置时重建）
{
    // 段内 [offset, offset+samples.Length) 就地写入（offset = 段内相对采样位置，0 = 段首）；
    // 宿主把 samples 拷进自有缓冲的该区间——span 借用语义，返回后插件可随意复用 / 池化该缓冲。
    // 越界（超出创建声明的 sampleCount）非法。可多次写、可覆盖重写、可在 Commit 后再写（重回未提交态）。
    // 写入区间即"该子区间已更新"信号（将来段内局部 effect 重渲据此；当前整段失效暂不消费区间）。
    void Write(int offset, ReadOnlySpan<float> samples);

    // 标该段音频已固定——送 effect 的唯一闸门。Commit 前的写入只供进度 / 波形展示，冻结数据（Commit）
    // 才进 effect，故合成爆发期不会拖着昂贵 effect 频繁重合成（闸门在协议层，非宿主防抖）。
    void Commit();
}
