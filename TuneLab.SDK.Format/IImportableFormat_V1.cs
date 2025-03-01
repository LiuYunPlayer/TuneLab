using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.SDK.Format;

public interface IImportableFormat_V1
{
    string Extension { get; }
    ProjectInfo_V1 Deserialize(Stream stream);
}
