using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `project open` 的封条。这条命令的价值几乎全在**它什么时候拒绝**：它换掉的是用户此刻正看着的
// 那份文档，一次判断失手就是把人家没保存的活儿扔了。故这里钉四条拒绝路径的顺序与措辞：
// 没有编辑器 → 文件不在 / 格式没人读 → **有未保存改动**（早于授权，不靠一次确认糊过去）→ 授权。
public class ProjectOpenCommandTests
{
    static readonly ProjectOpenCommand Command = new();

    // 「.tlpx 能不能读」这一问由 FormatsManager 答，而它的内建格式只在宿主启动时注册过——无 UI 的
    // 测试进程里没人注册，故这里补上（重复注册无副作用；同 TestVoices 的做法）。
    static ProjectOpenCommandTests()
    {
        TuneLab.Extensions.Formats.FormatsManager.LoadBuiltIn();
    }

    // 「有编辑器」的语境：换工程这件事只有真有窗口的宿主给得出（见 IProjectFileAccess）。
    sealed class StubProjectFile : IProjectFileAccess
    {
        public bool Saved = true;
        public string? Opened;
        public string? Error;

        public bool IsSaved => Saved;
        public string? Path => Opened;
        public string? Open(string path)
        {
            if (Error != null)
                return Error;
            Opened = path;
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

    sealed class RejectingPolicy : IAuthorizationPolicy
    {
        public AuthorizationMode Mode => AuthorizationMode.Confirm;
        public bool CanAsk => true;
        public AuthorizationRequest? Asked;
        public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        {
            Asked = request;
            return Task.FromResult(AuthorizationDecision.Reject);
        }
    }

    static CommandResult Run(string path, CommandContext ctx)
        => Command.ExecuteAsync(CommandArgs.Parse(new JsonObject { ["path"] = path }.ToJsonString()), ctx, CancellationToken.None)
            .GetAwaiter().GetResult();

    // 随手造一个真实存在的 .tlpx 空文件：这条命令在装载之前只看"存不存在 + 扩展名有没有人读"，
    // 故内容不必是真工程（装载失败那一支由 StubProjectFile.Error 模拟）。
    static string TempProjectFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "tunelab-open-test-" + Path.GetRandomFileName() + ".tlpx");
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void WithoutAnEditorItSaysTheDocumentDoesNotExistHere()
    {
        var result = Run("C:/whatever.tlpx", new CommandContext { Authorization = new AutoPolicy() });

        Assert.True(result.IsError);
        Assert.Equal("no_editor", result.Error!.Value.Code);
        // 要说清是"这里永远没有"而不是"暂时没有"，且给出去哪做：attach / --project。
        Assert.Contains("attach", result.Error!.Value.Message);
        Assert.Contains("--project", result.Error!.Value.Message);
    }

    [Fact]
    public void MissingFileIsReportedWithoutBotheringTheUser()
    {
        var policy = new RejectingPolicy();
        var result = Run(Path.Combine(Path.GetTempPath(), "definitely-not-here.tlpx"),
            new CommandContext { ProjectFile = new StubProjectFile(), Authorization = policy });

        Assert.True(result.IsError);
        Assert.Equal("missing_file", result.Error!.Value.Code);
        Assert.Null(policy.Asked);   // 坏请求不该打扰用户
    }

    // 未保存**先于授权**：卡片上写的是"打开哪个文件"，用户在那儿点"允许"并没有同意丢掉自己的活儿。
    [Fact]
    public void UnsavedChangesRefuseBeforeAskingAnyone()
    {
        var policy = new RejectingPolicy();
        var file = new StubProjectFile { Saved = false };
        var result = Run(TempProjectFile(), new CommandContext { ProjectFile = file, Authorization = policy });

        Assert.True(result.IsError);
        Assert.Equal("unsaved_changes", result.Error!.Value.Code);
        Assert.Contains("file.save", result.Error!.Value.Message);   // 指出去哪保存
        Assert.Null(policy.Asked);
        Assert.Null(file.Opened);
    }

    [Fact]
    public void RefusedAuthorizationIsAnHonestResultNotAnError()
    {
        var policy = new RejectingPolicy();
        var file = new StubProjectFile();
        var path = TempProjectFile();
        var result = Run(path, new CommandContext { ProjectFile = file, Authorization = policy });

        Assert.False(result.IsError);
        Assert.Equal("refused", result.Data!["outcome"]!.GetValue<string>());
        Assert.Null(file.Opened);
        // 闸门问的是这一档、且卡片上带着那个路径（用户得看见要打开的是哪一份）。
        Assert.Equal(WriteKind.ProjectOpen, policy.Asked!.Value.Kind);
        Assert.Equal(path, policy.Asked!.Value.Target);
    }

    [Fact]
    public void OpeningReportsWhatIsNowInFront()
    {
        var file = new StubProjectFile();
        var path = TempProjectFile();
        var result = Run(path, new CommandContext { ProjectFile = file, Authorization = new AutoPolicy() });

        Assert.False(result.IsError);
        Assert.Equal("applied", result.Data!["outcome"]!.GetValue<string>());
        Assert.Equal(path, file.Opened);
        Assert.Contains("Opened", Command.Render(result.Data, CommandArgs.Empty));
    }

    // 装载失败要说清"原来那份没动"——否则调用方无从判断现在手里是哪个工程。
    [Fact]
    public void LoadFailureSaysTheOldProjectIsUntouched()
    {
        var file = new StubProjectFile { Error = "unsupported file" };
        var result = Run(TempProjectFile(), new CommandContext { ProjectFile = file, Authorization = new AutoPolicy() });

        Assert.False(result.IsError);
        Assert.Equal("failed", result.Data!["outcome"]!.GetValue<string>());
        var text = Command.Render(result.Data, CommandArgs.Empty);
        Assert.Contains("unsupported file", text);
        Assert.Contains("untouched", text);
    }
}
