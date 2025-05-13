using System.IO;
using TuneLab.Core.DataInfo;

namespace TuneLab.Extensions.Format;

public interface IImportableFormat
{
    ProjectInfo Deserialize(Stream stream);
}
