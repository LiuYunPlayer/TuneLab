using System.IO;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Extensions.Formats;

public interface IImportFormat
{
    ProjectInfo Deserialize(Stream stream);
}
