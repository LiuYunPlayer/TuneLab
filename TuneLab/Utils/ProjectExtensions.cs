using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.SDK;

namespace TuneLab.Utils;

// 依赖数据层(IProject)的工程扩展——从通用的 GUI 层 Extensions 中拆出，留在主程序。
internal static class ProjectExtensions
{
    public static void NewTrack(this IProject project)
    {
        project.AddTrack(new TrackInfo() { Name = "Track".Tr(TC.Document) + "_" + (project.Tracks.Count + 1), Color = Style.GetNewColor(project.Tracks.Count) });
    }

    public static int PartsCount(this IProject project)
    {
        int count = 0;
        foreach (var track in project.Tracks)
        {
            count += track.Parts.Count;
        }
        return count;
    }
}
