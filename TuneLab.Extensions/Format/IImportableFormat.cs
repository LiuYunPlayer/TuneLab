using System.IO;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Extensions.Format;

internal interface IImportableFormat
{
    string Extension { get; }
    ProjectInfo Deserialize(Stream stream);
}
