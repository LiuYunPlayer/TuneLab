using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using TuneLab.Data;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// part preset 外部面的封条。
//
// 【这里【不】碰磁盘】preset 存在用户的配置目录里（PresetConfigManager 直接读 PathManager），而开发机
// 与 CI 上那个目录里有什么是不可预期的，写它更是不能接受。故这里只钉不依赖磁盘的那几半：
// 定位（哪个 part）、校验（名字合法性）、闸门措辞（用户凭它决定放不放行）、渲染（人与模型读到的话）。
// "真的存下来 / 真的应用上去"由真实例实测覆盖——那条链路的关键不是这几个分支，而是与侧栏共用
// 同一份 PartPresets（应用语义细到默认值算不算声音，两条路分叉就是同一条 preset 出两种声音）。
public class PresetCommandsTests
{
    static PresetCommandsTests()
    {
        TestVoices.EnsureBuiltIn();
    }

    // 一轨两 part：第一个是 midi part，第二个是音频 part（preset 对它无意义，要能分辨出来）。
    static IProject SampleProject()
    {
        var track = new NativeTrackInfo { Name = "vocal" };
        track.Parts.Add(new MidiPartInfo { Name = "p", Pos = 0, StartOffset = 0, EndOffset = 1920 });
        track.Parts.Add(new AudioPartInfo { Name = "a", Pos = 1920, Path = "nowhere.wav" });

        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo
        {
            Tempos = { new TempoInfo { Pos = 0, Bpm = 120 } },
            TimeSignatures = { new TimeSignatureInfo { BarIndex = 0, Numerator = 4, Denominator = 4 } },
            Tracks = { track },
        }));
        return document.Project!;
    }

    static CommandResult Run(ICommand command, string argumentsJson, IProject? project)
        => command.ExecuteAsync(CommandArgs.Parse(argumentsJson), new CommandContext { Project = project }, CancellationToken.None)
            .GetAwaiter().GetResult();

    // ── 定位：四种走不通各有各的下一步，故分开说

    [Fact]
    public void ApplySaysThereIsNoProjectInsteadOfBlamingTheNumbers()
    {
        var result = Run(new PresetApplyCommand(), """{"track": 1, "part": 1, "name": "x"}""", null);

        Assert.True(result.IsError);
        Assert.Equal("no_such_part", result.Error!.Value.Code);
        Assert.Contains("No project is open", result.Error!.Value.Message);
    }

    [Fact]
    public void ApplyTellsYouHowManyTracksAndPartsThereActuallyAre()
    {
        var project = SampleProject();

        var noTrack = Run(new PresetApplyCommand(), """{"track": 3, "part": 1}""", project);
        Assert.Equal("no_such_part", noTrack.Error!.Value.Code);
        Assert.Contains("no track 3 — the project has 1", noTrack.Error!.Value.Message);

        var noPart = Run(new PresetApplyCommand(), """{"track": 1, "part": 9}""", project);
        Assert.Contains("Track 1 has no part 9 — it has 2", noPart.Error!.Value.Message);
    }

    // 音频 part 不是"找不到"：它就在那儿，只是没有声源/属性/自动化。混成"找不到"会让调用方去数编号。
    [Fact]
    public void ApplyExplainsWhyAnAudioPartCannotTakeAPreset()
    {
        var result = Run(new PresetApplyCommand(), """{"track": 1, "part": 2}""", SampleProject());

        Assert.Equal("no_such_part", result.Error!.Value.Code);
        Assert.Contains("is an audio part", result.Error!.Value.Message);
        Assert.Contains("no sound source", result.Error!.Value.Message);
    }

    // 名字即文件名，故非法名字在**碰任何东西之前**就挡掉，且说的是同一句话（与界面共用校验）。
    [Fact]
    public void SaveRefusesANameThatCannotBeAFileName()
    {
        var result = Run(new PresetSaveCommand(), """{"track": 1, "part": 1, "name": "a/b"}""", SampleProject());

        Assert.True(result.IsError);
        Assert.Equal("invalid_name", result.Error!.Value.Code);
        Assert.Contains("cannot contain", result.Error!.Value.Message);
    }

    // ── 闸门：用户凭这句话决定放不放行，故四种后果必须说得不一样

    [Fact]
    public void TheGateSaysWhichConsequenceItIsAskingAbout()
    {
        var apply = new AuthorizationRequest(WriteKind.PresetApply, 0, "Warm", SecondaryTarget: "part 1 of track 1").ActionPhrase();
        Assert.Contains("apply the preset \"Warm\" to part 1 of track 1", apply);
        // 关键的一句：改的是声音，不是内容——用户最怕的是"我的音符/曲线没了"。
        Assert.Contains("not what it sings", apply);
        Assert.Contains("undo takes it back", apply);

        var reset = new AuthorizationRequest(WriteKind.PresetApply, 0, null, SecondaryTarget: "part 2 of track 3").ActionPhrase();
        Assert.Contains("reset part 2 of track 3 to its sound source's defaults", reset);

        var overwrite = new AuthorizationRequest(WriteKind.PresetOverwrite, 0, "Warm").ActionPhrase();
        Assert.Contains("replace the existing preset \"Warm\"", overwrite);

        var delete = new AuthorizationRequest(WriteKind.PresetDelete, 0, "Warm").ActionPhrase();
        Assert.Contains("the file is gone for good", delete);
        Assert.Contains("undo history does not cover it", delete);

        var rename = new AuthorizationRequest(WriteKind.PresetRename, 0, "Warm", "Warmer").ActionPhrase();
        Assert.Contains("rename the preset \"Warm\" to \"Warmer\"", rename);
    }

    // ── 渲染：Data 与文本是两半，这里钉住文本那一半

    [Fact]
    public void ListSaysWhatEachPresetHoldsAndWhatItDoesNot()
    {
        var data = new JsonObject
        {
            ["presets"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Warm",
                    ["source"] = new JsonObject { ["kind"] = "voice", ["type"] = "X", ["id"] = "alice", ["label"] = "X / alice" },
                    ["properties"] = 4,
                    ["automations"] = 7,
                },
            },
        };

        var text = new PresetListCommand().Render(data, CommandArgs.Empty);
        Assert.StartsWith("1 part preset(s).", text);
        Assert.Contains("no curves, no notes", text);
        Assert.Contains("\n- \"Warm\" — X / alice, 4 properties, 7 automation defaults", text);
    }

    [Fact]
    public void ListPointsAtHowToMakeOneWhenThereAreNone()
    {
        var text = new PresetListCommand().Render(new JsonObject { ["presets"] = new JsonArray() }, CommandArgs.Empty);
        Assert.Equal("No part presets are saved. Make one from a part with save_preset.", text);
    }

    // 应用之后最要紧的两句：动了多少，以及**没动的是什么**——调用方与用户都会怕它把曲线一起换掉。
    [Fact]
    public void ApplyReportsWhatItDidAndWhatItLeftAlone()
    {
        var applied = new PresetApplyCommand().Render(new JsonObject
        {
            ["outcome"] = "applied",
            ["name"] = "Warm",
            ["where"] = "part 1 of track 1",
            ["source"] = "X / alice",
            ["properties"] = 4,
            ["automations"] = 7,
            ["note"] = null,
        }, CommandArgs.Empty);
        Assert.Equal("Applied the preset \"Warm\" (X / alice) to part 1 of track 1: 4 properties, 7 automation defaults."
            + " The notes, curves and phonemes are untouched; Ctrl+Z takes it back.", applied);

        var reset = new PresetApplyCommand().Render(new JsonObject
        {
            ["outcome"] = "applied",
            ["name"] = null,
            ["where"] = "part 1 of track 1",
            ["source"] = null,
            ["properties"] = 1,
            ["automations"] = 13,
            ["note"] = null,
        }, CommandArgs.Empty);
        Assert.StartsWith("Reset part 1 of track 1 to its sound source's defaults: 1 property, 13 automation defaults.", reset);
        Assert.Contains("The sound source itself is unchanged.", reset);
    }

    // 声源一个属性都不声明时（换过引擎的 part 上很常见），"已重置"是句空话——那正是让人以为
    // 重置坏掉了的地方，故这一支必须如实说"什么都没变"，并点出它本来也不动声源。
    [Fact]
    public void ResettingSaysNothingChangedWhenTheSourceDeclaresNothing()
    {
        var text = new PresetApplyCommand().Render(new JsonObject
        {
            ["outcome"] = "applied",
            ["name"] = null,
            ["where"] = "part 1 of track 1",
            ["source"] = null,
            ["properties"] = 0,
            ["automations"] = 0,
            ["note"] = null,
        }, CommandArgs.Empty);

        Assert.Contains("nothing changed", text);
        Assert.Contains("declares no part properties", text);
        Assert.Contains("never changes the sound source itself", text);
        Assert.Contains("left over from other engines stay as they are", text);
    }

    // 新建与替换要说得不一样：后者动了用户已有的东西。
    [Fact]
    public void SaveDistinguishesCreatingFromReplacing()
    {
        JsonObject Data(string outcome) => new()
        {
            ["outcome"] = outcome,
            ["name"] = "Warm",
            ["where"] = "part 1 of track 1",
            ["source"] = "X / alice",
            ["properties"] = 4,
            ["automations"] = 7,
            ["note"] = null,
        };

        Assert.StartsWith("Saved the preset \"Warm\"", new PresetSaveCommand().Render(Data("created"), CommandArgs.Empty));
        Assert.StartsWith("Replaced the preset \"Warm\"", new PresetSaveCommand().Render(Data("replaced"), CommandArgs.Empty));
    }

    // 闸门拒绝时【只回它的原话】：命令自己不再补一句"已完成"式的话。
    [Fact]
    public void ARefusedWriteRendersTheGatesOwnWords()
    {
        var refused = new JsonObject { ["outcome"] = "refused", ["note"] = "The user chose NOT to allow it." };

        Assert.Equal("The user chose NOT to allow it.", new PresetApplyCommand().Render(refused, CommandArgs.Empty));
        Assert.Equal("The user chose NOT to allow it.", new PresetDeleteCommand().Render(refused, CommandArgs.Empty));
        Assert.Equal("The user chose NOT to allow it.", new PresetRenameCommand().Render(refused, CommandArgs.Empty));
    }
}
