using TuneLab.Extensions.Derivers;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// AudioPartInfo 的【宿主内部】子类，携带派生记录账本（键 = 内容寻址缓存 key）。
// 派生记录是宿主 / native-only 概念、随功能演进，故不上 SDK 公共 AudioPartInfo（保持冻结面干净、
// 通用格式插件无需知道派生）。用子类让记录随 part 在 ProjectInfo 里多态流转——无需 part id、无需位置索引。
// 通用格式插件拿到的是基类 AudioPartInfo、看不见记录（非 native 导出自然丢弃，正确）；仅 native(.tlp/.tlpx)
// 序列化器 downcast 读写。运行时 AudioPart 以普通【非撤销】集合持有这些记录（增删不进回退栈）。
internal sealed class NativeAudioPartInfo : AudioPartInfo
{
    public Map<string, DerivationRecordInfo> DerivationRecords { get; set; } = new();
}
