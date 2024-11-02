using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Extensions.Format;

internal interface IExportableFormat
{
    string Extension { get; }
    Stream Serialize(ProjectInfo info);
}
