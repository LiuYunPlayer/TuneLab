using System.IO;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Extensions.Formats;

public interface IExportFormat
{
    Stream Serialize(ProjectInfo info);
}
