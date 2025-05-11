using System.IO;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Extensions.Format;

internal interface IExportableFormat
{
    string Extension { get; }
    Stream Serialize(ProjectInfo info);
}
