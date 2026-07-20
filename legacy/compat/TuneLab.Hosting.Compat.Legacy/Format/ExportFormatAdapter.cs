using System.IO;
using LFmt = TuneLab.Extensions.Formats;
using VFmt = TuneLab.SDK;
using VFmtData = TuneLab.SDK;

namespace TuneLab.Hosting.Compat.Legacy.Format;

// 把老 IExportFormat 适配成 V1 IExportFormat：V1 ProjectInfo 边界深拷成老形态后委托给老插件序列化。
// 老契约仍是返回 Stream（旧形态不改），此处把老插件产出的流拷入宿主给的 output（新契约见 VFmt.IExportFormat）。
internal sealed class ExportFormatAdapter(LFmt.IExportFormat legacy) : VFmt.IExportFormat
{
    public void Serialize(Stream output, VFmtData.ProjectInfo info)
    {
        using var legacyStream = legacy.Serialize(info.ToLegacy());
        legacyStream.CopyTo(output);
    }
}
