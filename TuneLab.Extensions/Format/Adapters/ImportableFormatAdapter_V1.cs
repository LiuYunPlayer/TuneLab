using System.IO;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.SDK.Format;

namespace TuneLab.Extensions.Format.Adapters;

internal class ImportableFormatAdapter_V1(IImportableFormat_V1 importableFormat) : IImportableFormat
{
    public string Extension => importableFormat.Extension;

    public ProjectInfo Deserialize(Stream stream)
    {
        var projectInfo_V1 = importableFormat.Deserialize(stream);
        return projectInfo_V1.Convert();
    }
}
