using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.SDK.Format;

public interface IExportableFormat_V1
{
    string Extension { get; }
    Stream Serialize(ProjectInfo_V1 info);
}
