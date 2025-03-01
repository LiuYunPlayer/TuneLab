using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.Document;

namespace TuneLab.Data;

internal interface ITempo : IDataObject<TempoInfo>
{
    double Pos { get; }
    double Bpm { get; }
}
