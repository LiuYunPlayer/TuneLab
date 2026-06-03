using System.IO;
using LFmt = TuneLab.Extensions.Formats;
using VFmt = TuneLab.SDK.Format;
using VFmtData = TuneLab.SDK.Format.DataInfo;

namespace TuneLab.Hosting.Compat.Legacy.Format;

// 把老 IImportFormat 适配成 V1 IImportFormat：解析委托给老插件，结果 ProjectInfo 边界深拷成 V1（冷路径）。
internal sealed class ImportFormatAdapter(LFmt.IImportFormat legacy) : VFmt.IImportFormat
{
    public VFmtData.ProjectInfo Deserialize(Stream stream) => legacy.Deserialize(stream).ToV1();
}
