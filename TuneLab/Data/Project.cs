using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Data;
using TuneLab.Base.Structures;
using TuneLab.Utils;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Base.Utils;

namespace TuneLab.Data;

internal class Project : DataObject, IProject
{
    public ITempoManager TempoManager => mTempoManager;
    public ITimeSignatureManager TimeSignatureManager => mTimeSignatureManager;
    public IReadOnlyDataObjectList<ITrack> Tracks => mTracks;

    public Project() : this(new ProjectInfo()) { }
    public Project(ProjectInfo info)
    {
        mTempoManager = new(this);
        mTracks = new(this);

        mTracks.ItemAdded.Subscribe(OnTrackAdded);
        mTracks.ItemRemoved.Subscribe(OnTrackRemoved);

        IDataObject<ProjectInfo>.SetInfo(this, info);
    }

    public ProjectInfo GetInfo()
    {
        ProjectInfo info = new();

        info.Tempos = mTempoManager.GetInfo();

        for (int i = 0; i < mTimeSignatureManager.TimeSignatures.Count; i++)
        {
            var timeSignature = mTimeSignatureManager.TimeSignatures[i];
            info.TimeSignatures.Add(new TimeSignatureInfo
            {
                BarIndex = timeSignature.BarIndex,
                Numerator = timeSignature.Numerator,
                Denominator = timeSignature.Denominator,
            });
        }

        info.Tracks = mTracks.GetInfo().ToInfo();

        return info;
    }

    [MemberNotNull(nameof(mTimeSignatureManager))]
    void IDataObject<ProjectInfo>.SetInfo(ProjectInfo info)
    {
        // TODO: 两个manager都改成dataobject
        IDataObject<ProjectInfo>.SetInfo(mTempoManager, info.Tempos);
        mTimeSignatureManager = new TimeSignatureManager(info.TimeSignatures);
        IDataObject<ProjectInfo>.SetInfo(mTracks, info.Tracks.Convert(CreateTrack).ToArray());
    }

    public void AddTrack(TrackInfo info)
    {
        mTracks.Add(CreateTrack(info));
    }

    public void RemoveTrack(ITrack track)
    {
        mTracks.Remove(track);
    }

    public void RemoveTrackAt(int trackIndex)
    {
        if ((uint)trackIndex >= mTracks.Count)
            return;

        mTracks.RemoveAt(trackIndex);
    }

    Track CreateTrack(TrackInfo info)
    {
        return new Track(this, info);
    }

    void OnTrackAdded(ITrack track)
    {
        track.Activate();
        AudioEngine.AddTrack(track);
    }

    void OnTrackRemoved(ITrack track)
    {
        AudioEngine.RemoveTrack(track);
        track.Deactivate();
    }

    public void Dispose()
    {
        foreach (var track in mTracks)
        {
            track.Deactivate();
        }
    }

    TempoManager mTempoManager;
    TimeSignatureManager mTimeSignatureManager;
    readonly DataObjectList<ITrack> mTracks;
}
