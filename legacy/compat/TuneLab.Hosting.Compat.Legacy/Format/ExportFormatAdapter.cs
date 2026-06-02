using System.IO;
using LFmt = TuneLab.Extensions.Formats;
using VFmt = TuneLab.SDK.Format;
using VFmtData = TuneLab.SDK.Format.DataInfo;

namespace TuneLab.Hosting.Compat.Legacy.Format;

// 把老 IExportFormat 适配成 V1 IExportFormat：V1 ProjectInfo 边界深拷成老形态后委托给老插件序列化。
internal sealed class ExportFormatAdapter(LFmt.IExportFormat legacy) : VFmt.IExportFormat
{
    public Stream Serialize(VFmtData.ProjectInfo info) => legacy.Serialize(info.ToLegacy());
}
