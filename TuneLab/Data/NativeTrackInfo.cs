using TuneLab.SDK;

namespace TuneLab.Data;

// TrackInfo 的【宿主内部】子类，携带逐轨导出开关。
// 导出配置是 app 私有状态、不属通用 musical 交换契约：工程级那 8 项早已内部化成 ExportConfigInfo（组合进
// NativeProjectFile），但逐轨这两项当时漏在了 SDK 公共 TrackInfo 上——于是每个通用格式插件都能读写它们，
// 与「通用 IImport/ExportFormat 保持 musical-only」相悖。此处按 docs/sdk-api-evolution.md 的判据补齐：
// 数据要穿过宿主不拥有元素类型的公共集合（逐轨于 ProjectInfo.Tracks 这个 List<TrackInfo>），故用【子类】
// 而非组合——随集合多态流转，共位即免费身份（无需 track id，位置索引会脆）。
// 通用格式插件拿到的是基类 TrackInfo、看不见这两项（非 native 导出自然丢弃，正确）；仅 native(.tlp/.tlpx)
// 序列化器 downcast 读写。运行时 ITrack 仍以普通【非撤销】属性持有（导出开关是设置项，不入回退栈）。
internal sealed class NativeTrackInfo : TrackInfo
{
    public bool ExportEnabled { get; set; } = false;
    public int ExportChannels { get; set; } = 1;   // 1 = mono, 2 = stereo
}
