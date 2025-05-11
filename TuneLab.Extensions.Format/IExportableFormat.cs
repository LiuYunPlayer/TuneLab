using System.IO;
using TuneLab.Core.DataInfo;

namespace TuneLab.Extensions.Format;

public interface IExportableFormat
{
    string FileExtension { get; }
    Stream Serialize(ProjectInfo info);
}
