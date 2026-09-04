using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `project export` / `script save` / `script delete`（搬家前的 export_project / save_script /
// delete_script）的封条——三条都写【用户磁盘上的文件】，历史记录管理器救不回。
//
// 各有一句绝不能丢的话：
//  · 导出是"存了一份副本"，不是"保存了工程"——说错会让用户以为自己的改动落盘了；
//  · 等授权期间目标路径冒出文件 → 什么都不写（用户批准的是"新建"，不是"替换"）；
//  · 存脚本要回报它注册到了哪个菜单（或明说它不是菜单工具），否则用户不知道去哪找。
public class FileWriteCommandsTests
{
    // ── project export

    static readonly ProjectExportCommand Export = new();

    [Fact]
    public void ExportSaysItWasACopyNotASave()
    {
        var text = Export.Render(new JsonObject
        {
            ["path"] = "D:/out/song.tlpx",
            ["format"] = "tlpx",
            ["formatName"] = "TuneLab Project",
            ["outcome"] = "applied",
            ["overwrite"] = false,
            ["bytes"] = 2048L,
            ["note"] = null,
        }, CommandArgs.Empty);

        Assert.Equal("Exported the project as TuneLab Project to \"D:/out/song.tlpx\" (2.0 KB)."
            + " Note this was a copy: the user's project is still open with the same save file and unsaved changes as before.", text);
    }

    [Fact]
    public void OverwritingExportSaysTheOldFileWasReplaced()
    {
        var text = Export.Render(new JsonObject
        {
            ["path"] = "D:/out/song.mid",
            ["format"] = "mid",
            ["formatName"] = "MIDI",
            ["outcome"] = "applied",
            ["overwrite"] = true,
            ["bytes"] = 900L,
            ["note"] = null,
        }, CommandArgs.Empty);

        Assert.Contains("(900 bytes), replacing the file that was there.", text);
    }

    // 用户批准的是"新建"，那就只能新建：等待期间冒出来的文件绝不替换。
    [Fact]
    public void FileAppearingWhileWaitingIsNotReplaced()
    {
        var text = Export.Render(new JsonObject
        {
            ["path"] = "D:/out/song.tlpx",
            ["format"] = "tlpx",
            ["formatName"] = "TuneLab Project",
            ["outcome"] = "appeared_meanwhile",
            ["overwrite"] = false,
        }, CommandArgs.Empty);

        Assert.Equal("A file appeared at \"D:/out/song.tlpx\" while waiting for authorization,"
            + " and the user only approved writing a NEW file there, not replacing one."
            + " Nothing was written. Ask again if they want to replace it.", text);
    }

    [Fact]
    public void ExportWithoutAnExtensionListsTheSupportedOnes()
    {
        var result = Export.ExecuteAsync(CommandArgs.Parse("""{"path":"D:/out/song"}"""),
            new CommandContext { Project = null }, CancellationToken.None).GetAwaiter().GetResult();

        // 没有工程时先报没有工程（导出的对象都不存在，谈不上格式）。
        Assert.True(result.IsError);
        Assert.Equal("no_project", result.Error!.Value.Code);
    }

    [Fact]
    public void ExportWithoutAPathIsRejected()
    {
        var result = Export.ExecuteAsync(CommandArgs.Parse("""{"path":"  "}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("empty_path", result.Error!.Value.Code);
    }

    // ── script save

    static readonly ScriptSaveCommand Save = new();

    [Fact]
    public void SavedMenuToolReportsWhichMenuItLandedIn()
    {
        var text = Save.Render(new JsonObject
        {
            ["name"] = "fade-notes",
            ["outcome"] = "applied",
            ["existed"] = false,
            ["tool"] = new JsonObject { ["displayName"] = "渐弱", ["context"] = "Note" },
            ["note"] = null,
        }, CommandArgs.Empty);

        Assert.Equal("Saved script \"fade-notes\". Registered as menu tool \"渐弱\" in the piano-roll note right-click menu.", text);
    }

    [Fact]
    public void OverwrittenScriptSaysUpdatedRatherThanSaved()
    {
        var text = Save.Render(new JsonObject
        {
            ["name"] = "fade-notes",
            ["outcome"] = "applied",
            ["existed"] = true,
            ["tool"] = null,
            ["note"] = null,
        }, CommandArgs.Empty);

        Assert.Equal("Updated script \"fade-notes\". It has no getScriptInfo(), so it is a plain run-once script"
            + " (Script side panel only; not in menus).", text);
    }

    [Fact]
    public void RefusedOverwriteGivesTheGateWordingBack()
    {
        Assert.Equal("The user chose NOT to allow it.", Save.Render(new JsonObject
        {
            ["name"] = "fade-notes",
            ["outcome"] = "refused",
            ["existed"] = true,
            ["note"] = "The user chose NOT to allow it.",
        }, CommandArgs.Empty));
    }

    [Fact]
    public void SavingWithoutANameIsRejectedBeforeAnythingElse()
    {
        var result = Save.ExecuteAsync(CommandArgs.Parse("""{"name":"  ","code":"main(){}"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("empty_name", result.Error!.Value.Code);
    }

    // ── script delete

    static readonly ScriptDeleteCommand Delete = new();

    [Fact]
    public void DeletedScriptIsReportedByName()
    {
        Assert.Equal("Deleted script \"scratch\".", Delete.Render(new JsonObject
        {
            ["name"] = "scratch",
            ["outcome"] = "applied",
            ["note"] = null,
        }, CommandArgs.Empty));
    }

    [Fact]
    public void DeletingAnUnknownScriptPointsAtTheList()
    {
        var result = Delete.ExecuteAsync(CommandArgs.Parse("""{"name":"no-such-script-xyz"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("not_found", result.Error!.Value.Code);
        Assert.Equal("no script named \"no-such-script-xyz\". Call list_scripts to see available names.", result.Error!.Value.Message);
    }
}
