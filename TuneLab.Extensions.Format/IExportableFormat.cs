using System.IO;
using TuneLab.Core.DataInfo;

namespace TuneLab.Extensions.Format;

public interface IExportableFormat
{
    Stream Serialize(ProjectInfo info);
}
