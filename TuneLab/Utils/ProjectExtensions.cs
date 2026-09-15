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

    // 删除全部选中轨道（右键菜单与 Delete 键共用一份，镜像 part 侧的 DeleteAllSelectedParts）：
    // 一次提交 = 一步撤销，整批要回来就一次回来。
    public static void DeleteAllSelectedTracks(this IProject project)
    {
        var selected = project.Tracks.AllSelectedItems();
        if (selected.Count == 0)
            return;

        foreach (var track in selected)
        {
            project.RemoveTrack(track);
        }
        project.Commit();
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
