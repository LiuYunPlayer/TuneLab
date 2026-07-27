using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Foundation;
using TuneLab.Utils;
using TuneLab.SDK;

namespace TuneLab.Data;

internal class Project : DataObject, IProject
{
    public ITempoManager TempoManager => mTempoManager;
    public ITimeSignatureManager TimeSignatureManager => mTimeSignatureManager;
    public IReadOnlyDataObjectList<ITrack> Tracks => mTracks;
    public string ExportPath { get; set; } = string.Empty;
    public string ExportFileName { get; set; } = string.Empty;
    public string ExportFormat { get; set; } = "wav";
    public int ExportSampleRate { get; set; } = 44100;
    public int ExportBitDepth { get; set; } = 16;
    public int ExportBitrate { get; set; } = 320;
    public bool MasterExportEnabled { get; set; } = true;
    public int MasterExportChannels { get; set; } = 2;

    public Project() : this(new ProjectInfo()) { }
    public Project(ProjectInfo info)
    {
        mTimeSignatureManager = new(this);
        mTempoManager = new(this);
        mTracks = new(this);

        mTracks.ItemAdded.Subscribe(OnTrackAdded);
        mTracks.ItemRemoved.Subscribe(OnTrackRemoved);

        SetInfo(info);
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

    public void SetInfo(ProjectInfo info)
    {
        using var _ = MergeNotify();
        mTempoManager.SetInfo(info.Tempos);
        mTimeSignatureManager.SetInfo(info.TimeSignatures);
        mTracks.SetInfo(info.Tracks.Convert(CreateTrack).ToArray());
    }

    public void AddTrack(TrackInfo info)
    {
        mTracks.Add(CreateTrack(info));
    }

    public bool RemoveTrack(ITrack track)
    {
        return mTracks.Remove(track);
    }

    // 越界抛而非静默 no-op：按 .NET 惯例，按下标寻址的成员里"下标非法"是编程错误而非正常情形——
    // 这也正是 RemoveAt 无返回值的前提（Remove(item) 才需要 bool 表达"它原本在不在"）。
    // 宽容版会把 bug 藏起来：调用方既拿不到返回值、也收不到异常，彻底无从得知什么都没发生。
    public void RemoveTrackAt(int trackIndex)
    {
        if ((uint)trackIndex >= mTracks.Count)
            throw new ArgumentOutOfRangeException(nameof(trackIndex));

        mTracks.RemoveAt(trackIndex);
    }

    // 插入的合法下标含末位（== Count 即追加）。
    public void InsertTrack(int trackIndex, ITrack track)
    {
        if ((uint)trackIndex > mTracks.Count)
            throw new ArgumentOutOfRangeException(nameof(trackIndex));

        mTracks.Insert(trackIndex, track);
    }

    public Track CreateTrack(TrackInfo info)
    {
        return new Track(this, info);
    }

    ITrack IProject.CreateTrack(TrackInfo info) => CreateTrack(info);

    void OnTrackAdded(ITrack track)
    {
        track.Activate();
    }

    void OnTrackRemoved(ITrack track)
    {
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
