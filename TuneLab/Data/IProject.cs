using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Data;

internal interface IProject : IDataObject<ProjectInfo>, ITimeline, IDisposable
{
    IReadOnlyDataObjectList<ITrack> Tracks { get; }
    void AddTrack(TrackInfo info);
    void RemoveTrack(ITrack track);
}

internal static class IProjectExtension
{
    public static void NewTrack(this IProject project)
    {
        project.AddTrack(new TrackInfo() { Name = "Track_" + (project.Tracks.Count + 1) });
    }
}