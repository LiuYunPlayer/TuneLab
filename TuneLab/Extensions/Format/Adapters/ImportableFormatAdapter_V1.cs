using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.SDK.Base.DataStructures;
using TuneLab.SDK.Format;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.Extensions.Format.Adapters;

public class ImportableFormatAdapter_V1(IImportableFormat_V1 importableFormat) : IImportableFormat
{
    public string Extension => importableFormat.Extension;

    public ProjectInfo Deserialize(Stream stream)
    {
        var projectInfo_V1 = importableFormat.Deserialize(stream);
        return projectInfo_V1.Convert();
    }
}