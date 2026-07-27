using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

internal interface IProject : IDataObject<ProjectInfo>, ITimeline, IDisposable
{
    IReadOnlyDataObjectList<ITrack> Tracks { get; }
    string ExportPath { get; set; }
    string ExportFileName { get; set; }
    string ExportFormat { get; set; }
    int ExportSampleRate { get; set; }
    int ExportBitDepth { get; set; }
    int ExportBitrate { get; set; }
    bool MasterExportEnabled { get; set; }
    int MasterExportChannels { get; set; }
    void AddTrack(TrackInfo info);
    void RemoveTrack(ITrack track);
    void RemoveTrackAt(int index);
    void InsertTrack(int index, ITrack track);
}

internal static class IProjectExtension
{
    // IProject 的 8 个 Export*/MasterExport* 属性是导出状态的真源；以下两个宿主内部辅助在它们与 native 格式的
    // 宿主内部 ExportConfigInfo 之间互转（供 open/save 编排、agent 的 export_project 用；不经 SDK 公共面）。
    // 挂在 IProject 上（而非 Project 实例方法）：转换只读写接口已有的那 8 个属性，故凡持 IProject 者都能复用
    // 同一份映射、不必各自重拼——多一份重拼就多一处漏字段的机会。
    public static ExportConfigInfo GetExportConfig(this IProject project) => new()
    {
        ExportPath = project.ExportPath,
        FileName = project.ExportFileName,
        Format = project.ExportFormat,
        SampleRate = project.ExportSampleRate,
        BitDepth = project.ExportBitDepth,
        Bitrate = project.ExportBitrate,
        MasterExportEnabled = project.MasterExportEnabled,
        MasterExportChannels = project.MasterExportChannels,
    };

    public static void SetExportConfig(this IProject project, ExportConfigInfo config)
    {
        if (config == null)
            return;

        project.ExportPath = config.ExportPath;
        project.ExportFileName = config.FileName;
        project.ExportFormat = string.IsNullOrEmpty(config.Format) ? "wav" : config.Format;
        project.ExportSampleRate = config.SampleRate;
        project.ExportBitDepth = config.BitDepth;
        project.ExportBitrate = config.Bitrate;
        project.MasterExportEnabled = config.MasterExportEnabled;
        project.MasterExportChannels = config.MasterExportChannels;
    }

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