using System.IO;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.SDK.Format;

namespace TuneLab.Extensions.Format.Adapters;

internal class ExportableFormatAdapter_V1(IExportableFormat_V1 exportableFormat_V1) : IExportableFormat
{
    public string Extension => exportableFormat_V1.Extension;

    public Stream Serialize(ProjectInfo info)
    {
        var projectInfo_V1 = info.ConvertToV1();
        return exportableFormat_V1.Serialize(projectInfo_V1);
    }
}
