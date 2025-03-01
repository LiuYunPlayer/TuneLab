using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.Document;

namespace TuneLab.Data;

internal interface IPhoneme : IDataObject<PhonemeInfo>
{
    IDataProperty<double> StartTime { get; }
    IDataProperty<double> EndTime { get; }
    IDataProperty<string> Symbol { get; }
}
