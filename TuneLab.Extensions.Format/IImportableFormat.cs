using System.IO;
using TuneLab.Core.DataInfo;

namespace TuneLab.Extensions.Format;

public interface IImportableFormat
{
    string FileExtension { get; }
    ProjectInfo Deserialize(Stream stream);
}
