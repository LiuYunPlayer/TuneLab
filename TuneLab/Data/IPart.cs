using TuneLab.Audio;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;

namespace TuneLab.Data;

internal interface IPart : IReadOnlyDataObject<PartInfo>, ITimeline, IDuration, IAudioSource, ISelectable, ILinkedNode<IPart>
{
    ITrack Track { get; set; }
    new IPart? Next { get; }
    new IPart? Last { get; }
    IDataProperty<string> Name { get; }
    IDataProperty<double> Pos { get; }
    IDataProperty<double> Dur { get; }
    void Activate();
    void Deactivate();
}

internal static class IPartExtension
{
    public static double StartPos(this IPart part)
    {
        return part.Pos.Value;
    }

    public static double EndPos(this IPart part)
    {
        return part.Pos.Value + part.Dur.Value;
    }
}
