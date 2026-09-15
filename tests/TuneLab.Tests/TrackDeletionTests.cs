using System.Linq;
using TuneLab.Data;
using TuneLab.SDK;
using TuneLab.Utils;
using Xunit;

namespace TuneLab.Tests;

// 轨道删除的作用域与撤销粒度：删除（轨道头右键菜单 / Delete 键）作用于**全部选中轨道**，
// 且整批只占一步撤销——曾只删右键命中的那一条，多选后要逐条点。
[Collection("DataThreadProject")]
public class TrackDeletionTests
{
    // 建 MidiPart 就会向 VoicesManager 求声明 config，那条路以「空引擎」兜底，而它只在内建加载时注册。
    static TrackDeletionTests() => TestVoices.EnsureBuiltIn();

    static ProjectDocument Document()
    {
        var info = new ProjectInfo();
        for (int i = 0; i < 4; i++)
        {
            var track = new NativeTrackInfo { Name = "t" + i };
            track.Parts.Add(new MidiPartInfo { Name = "p" + i, Pos = 0, StartOffset = 0, EndOffset = 1920 });
            info.Tracks.Add(track);
        }
        var document = new ProjectDocument();
        document.SetProject(new Project(info));
        return document;
    }

    [Fact]
    public void DeleteAllSelectedTracks_RemovesEverySelectedTrack()
    {
        var document = Document();
        var project = document.Project!;
        var tracks = project.Tracks.ToList();
        tracks[1].Select();
        tracks[3].Select();

        project.DeleteAllSelectedTracks();

        Assert.Equal(["t0", "t2"], project.Tracks.Select(t => t.Name.Value));
    }

    [Fact]
    public void DeleteAllSelectedTracks_IsOneUndoStep()
    {
        var document = Document();
        var project = document.Project!;
        foreach (var track in project.Tracks.ToList().Take(3))
        {
            track.Select();
        }

        project.DeleteAllSelectedTracks();
        Assert.Single(project.Tracks);

        document.Undo();
        Assert.Equal(["t0", "t1", "t2", "t3"], project.Tracks.Select(t => t.Name.Value));
    }

    // 没有选中任何轨道时是纯 no-op：不删东西，也不在撤销栈里留一步空记录
    //（Delete 键会走到这里——编排区那边没有可删对象时同样什么都不该发生）。
    [Fact]
    public void DeleteAllSelectedTracks_WithoutSelection_DoesNothing()
    {
        var document = Document();
        var project = document.Project!;

        project.DeleteAllSelectedTracks();

        Assert.Equal(4, project.Tracks.Count);
        Assert.False(document.Undoable());
    }
}
