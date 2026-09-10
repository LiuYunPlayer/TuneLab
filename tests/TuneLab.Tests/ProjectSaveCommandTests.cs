using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `project save` / `project save-as` 的封条。
//
// 这两条与 `project export` 的差别是**语义**而不是路径：export 写一份副本、工程照旧指向老文件；
// 这两条是真的保存——改写用户的文件、把工程的保存路径挪过去、清掉未保存标记，撤销栈救不回其中任何
// 一件。故这里钉三样：三种走不通各说各的话（它们的下一步完全不同）、闸门措辞把"这不是副本"说出来、
// 回报如实（尤其"没存下"与"本来就不用存"这两种，绝不能读成"存好了"）。
//
// 【不碰磁盘】保存的落地在 Editor 里（IProjectFileAccess 转发），这里用替身把它的答案喂进来。
public class ProjectSaveCommandTests
{
    sealed class StubProjectFile : IProjectFileAccess
    {
        public bool IsSaved { get; set; }
        public string? Path { get; set; }
        public bool HasSaveTarget { get; set; }
        public string? OpenError { get; set; }
        public string? SaveError { get; set; }
        public int Saves;
        public string? SavedTo;

        string? IProjectFileAccess.Open(string path) => OpenError;

        public string? Save(string? path)
        {
            Saves++;
            SavedTo = path;
            if (SaveError != null)
                return SaveError;
            if (path != null)
                Path = path;
            IsSaved = true;
            HasSaveTarget = true;
            return null;
        }
    }

    sealed class AutoPolicy : IAuthorizationPolicy
    {
        public AuthorizationMode Mode => AuthorizationMode.Auto;
        public bool CanAsk => false;
        public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(AuthorizationDecision.ApplyOnce);
    }

    sealed class RecordingPolicy : IAuthorizationPolicy
    {
        public AuthorizationRequest? Asked;
        public AuthorizationMode Mode => AuthorizationMode.Confirm;
        public bool CanAsk => true;
        public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        {
            Asked = request;
            return Task.FromResult(AuthorizationDecision.ApplyOnce);
        }
    }

    static CommandResult Run(ICommand command, string argumentsJson, IProjectFileAccess? file, IAuthorizationPolicy? policy = null)
        => command.ExecuteAsync(CommandArgs.Parse(argumentsJson),
            new CommandContext { ProjectFile = file, Authorization = policy ?? new AutoPolicy() }, CancellationToken.None)
            .GetAwaiter().GetResult();

    // ── 三种走不通，各说各的话

    // headless 没有"用户打开着的文档"这个东西——不能含糊成"保存失败"，得指向那里真正该用的 export。
    [Fact]
    public void SavingNeedsAnEditorAndSaysWhatToUseInstead()
    {
        foreach (var result in new[]
        {
            Run(new ProjectSaveCommand(), "{}", null),
            Run(new ProjectSaveAsCommand(), """{"path": "C:\\x\\y.tlpx"}""", null),
        })
        {
            Assert.True(result.IsError);
            Assert.Equal("no_editor", result.Error!.Value.Code);
            Assert.Contains("export_project", result.Error!.Value.Message);
        }
    }

    // 从未保存过 → 不猜路径、不弹框，指向 save-as。
    [Fact]
    public void SaveRefusesWhenThereIsNoFileToSaveBackTo()
    {
        var file = new StubProjectFile { HasSaveTarget = false, IsSaved = false };
        var result = Run(new ProjectSaveCommand(), "{}", file);

        Assert.Equal("never_saved", result.Error!.Value.Code);
        Assert.Contains("save_project_as", result.Error!.Value.Message);
        Assert.Equal(0, file.Saves);
    }

    // 已经是保存状态就不写盘：不动用户的文件，也不谎称"我保存了什么"。
    [Fact]
    public void SaveDoesNotTouchTheFileWhenThereIsNothingToSave()
    {
        var file = new StubProjectFile { HasSaveTarget = true, IsSaved = true, Path = @"C:\songs\a.tlpx" };
        var result = Run(new ProjectSaveCommand(), "{}", file);

        Assert.False(result.IsError);
        Assert.Equal("already_saved", result.Data!["outcome"]!.GetValue<string>());
        Assert.Equal(0, file.Saves);
        Assert.Contains("Nothing to do", new ProjectSaveCommand().Render(result.Data, CommandArgs.Empty));
    }

    [Fact]
    public void SaveAsChecksThePathBeforeTouchingAnything()
    {
        var file = new StubProjectFile { HasSaveTarget = true, Path = @"C:\songs\a.tlpx" };

        var empty = Run(new ProjectSaveAsCommand(), """{"path": "  "}""", file);
        Assert.Equal("empty_path", empty.Error!.Value.Code);

        var missing = Run(new ProjectSaveAsCommand(), """{"path": "C:\\no\\such\\folder\\a.tlpx"}""", file);
        Assert.Equal("missing_folder", missing.Error!.Value.Code);
        Assert.Contains("Create it first", missing.Error!.Value.Message);
        Assert.Equal(0, file.Saves);
    }

    // ── 落地与回报

    [Fact]
    public void SaveWritesBackToItsOwnFileAndSaysSo()
    {
        var file = new StubProjectFile { HasSaveTarget = true, IsSaved = false, Path = @"C:\songs\a.tlpx" };
        var result = Run(new ProjectSaveCommand(), "{}", file);

        Assert.Equal(1, file.Saves);
        Assert.Null(file.SavedTo);   // null = 存回当前路径
        var text = new ProjectSaveCommand().Render(result.Data, CommandArgs.Empty);
        Assert.Contains(@"Saved the project to ""C:\songs\a.tlpx""", text);
        Assert.Contains("no longer unsaved", text);
    }

    // save-as 的要害是"从此以后存到那里"——回报必须把这句说出来，否则读起来与 export 一模一样。
    [Fact]
    public void SaveAsMovesWhereTheProjectLivesAndSaysThat()
    {
        var file = new StubProjectFile { HasSaveTarget = true, IsSaved = false, Path = @"C:\songs\a.tlpx" };
        var result = Run(new ProjectSaveAsCommand(), """{"path": "C:\\Windows\\Temp\\b.tlpx"}""", file);

        Assert.False(result.IsError, result.Error?.Message);
        Assert.Equal(1, file.Saves);
        Assert.Equal(@"C:\Windows\Temp\b.tlpx", file.SavedTo);
        Assert.Contains("From now on that is the file it saves to",
            new ProjectSaveAsCommand().Render(result.Data, CommandArgs.Empty));
    }

    // 写盘失败绝不能读成成功——回报要点出"工程仍然是未保存的"。
    [Fact]
    public void AFailedWriteSaysTheProjectIsStillUnsaved()
    {
        var file = new StubProjectFile { HasSaveTarget = true, IsSaved = false, Path = @"C:\songs\a.tlpx", SaveError = "the disk is full" };
        var result = Run(new ProjectSaveCommand(), "{}", file);

        var text = new ProjectSaveCommand().Render(result.Data, CommandArgs.Empty);
        Assert.Contains("Could NOT save", text);
        Assert.Contains("the disk is full", text);
        Assert.Contains("still unsaved", text);
    }

    // ── 闸门：用户凭这句话决定放不放行，故"这是真的保存、不是副本"必须在卡片上

    [Fact]
    public void TheGateSaysThisIsARealSaveAndNotACopy()
    {
        var policy = new RecordingPolicy();
        Run(new ProjectSaveCommand(), "{}", new StubProjectFile { HasSaveTarget = true, Path = @"C:\songs\a.tlpx" }, policy);
        Assert.Equal(WriteKind.ProjectSave, policy.Asked!.Value.Kind);
        Assert.Contains("not a copy", policy.Asked!.Value.ActionPhrase());
        Assert.Contains("overwriting what is in that file", policy.Asked!.Value.ActionPhrase());

        var asNew = new RecordingPolicy();
        Run(new ProjectSaveAsCommand(), """{"path": "C:\\Windows\\Temp\\brand-new-name.tlpx"}""",
            new StubProjectFile { HasSaveTarget = true, Path = @"C:\songs\a.tlpx" }, asNew);
        Assert.Equal(WriteKind.ProjectSaveAs, asNew.Asked!.Value.Kind);
        Assert.Contains("from then on that is the file", asNew.Asked!.Value.ActionPhrase());
    }

    // 另存撞上已有文件时单列一档：卡片上要出现"替换"，那是用户唯一能据此喊停的信息。
    [Fact]
    public void SavingOntoAnExistingFileSaysReplace()
    {
        var existing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tunelab-save-as-probe.tlpx");
        System.IO.File.WriteAllText(existing, "not a real project");
        try
        {
            var policy = new RecordingPolicy();
            Run(new ProjectSaveAsCommand(), $$"""{"path": "{{existing.Replace(@"\", @"\\")}}"}""",
                new StubProjectFile { HasSaveTarget = true, Path = @"C:\songs\a.tlpx" }, policy);

            Assert.Equal(WriteKind.ProjectSaveAsOverwrite, policy.Asked!.Value.Kind);
            Assert.Contains("REPLACING the file already there", policy.Asked!.Value.ActionPhrase());
        }
        finally
        {
            System.IO.File.Delete(existing);
        }
    }
}
