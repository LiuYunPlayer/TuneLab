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
    void RemoveTrackAt(int index);
    void InsertTrack(int index, ITrack track);
}

internal static class IProjectExtension
{
    public static IEnumerable<IPart> AllParts(this IProject project)
    {
        return project.Tracks.SelectMany(track => track.Parts);
    }

    public static IEnumerable<IMidiPart> AllMidiParts(this IProject project)
    {
        return project.AllParts().OfType<IMidiPart>();
    }

    public static IEnumerable<IAudioPart> AllAudioParts(this IProject project)
    {
        return project.AllParts().OfType<IAudioPart>();
    }

    public static void NewTrack(this IProject project)
    {
        project.AddTrack(new TrackInfo() { Name = "Track_" + (project.Tracks.Count + 1) });
    }

    public static void DisableAutoPrepare(this IProject project)
    {
        foreach (var part in project.AllMidiParts())
        {
            part.DisableAutoPrepare();
        }
    }

    public static void EnableAutoPrepare(this IProject project)
    {
        foreach (var part in project.AllMidiParts())
        {
            part.EnableAutoPrepare();
        }
    }

    public static void BeginMergeReSegment(this IProject project)
    {
        foreach (var part in project.AllMidiParts())
        {
            part.BeginMergeReSegment();
        }
    }

    public static void EndMergeReSegment(this IProject project)
    {
        foreach (var part in project.AllMidiParts())
        {
            part.EndMergeReSegment();
        }
    }

    public static void BeginMergeDirty(this IProject project)
    {
        foreach (var part in project.AllMidiParts())
        {
            part.BeginMergeDirty();
        }
    }

    public static void EndMergeDirty(this IProject project)
    {
        foreach (var part in project.AllMidiParts())
        {
            part.EndMergeDirty();
        }
    }
}