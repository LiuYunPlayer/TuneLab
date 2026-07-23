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

    // Project 的 8 个 Export*/MasterExport* 属性是导出状态的真源；以下两个宿主内部辅助在它们与 native 格式的
    // 宿主内部 ExportConfigInfo 之间互转（供 open/save 编排用；不经 SDK 公共面）。
    internal ExportConfigInfo GetExportConfig() => new()
    {
        ExportPath = ExportPath,
        FileName = ExportFileName,
        Format = ExportFormat,
        SampleRate = ExportSampleRate,
        BitDepth = ExportBitDepth,
        Bitrate = ExportBitrate,
        MasterExportEnabled = MasterExportEnabled,
        MasterExportChannels = MasterExportChannels,
    };

    internal void SetExportConfig(ExportConfigInfo config)
    {
        if (config == null)
            return;

        ExportPath = config.ExportPath;
        ExportFileName = config.FileName;
        ExportFormat = string.IsNullOrEmpty(config.Format) ? "wav" : config.Format;
        ExportSampleRate = config.SampleRate;
        ExportBitDepth = config.BitDepth;
        ExportBitrate = config.Bitrate;
        MasterExportEnabled = config.MasterExportEnabled;
        MasterExportChannels = config.MasterExportChannels;
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

    public void InsertTrack(int trackIndex, ITrack track)
    {
        if ((uint)trackIndex > mTracks.Count)
            return;

        mTracks.Insert(trackIndex, track);
    }

    Track CreateTrack(TrackInfo info)
    {
        return new Track(this, info);
    }

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
