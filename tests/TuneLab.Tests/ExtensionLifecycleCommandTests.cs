using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `extension install` / `uninstall` / `cancel-uninstall` 的封条。
//
// 这三条动的是**用户机器上的文件与第三方代码**，故这里钉两样：坏请求在打扰用户【之前】就被挡住
// （装一个不是 .tlx 的东西、卸一个没装过的包），以及授权卡片上那句话说的是不是实情——那句话是用户
// 唯一的判断依据，它含糊一次就等于没问。
//
// 装成功那一条不在这里：它要往用户的扩展目录真解压一个包并加载第三方代码，那是集成测试的活儿
// （而且本进程一个包都没加载过，`LoadResults` 是空的，故凡要"已装的包"的路径都只能验拒绝那一支）。
public class ExtensionLifecycleCommandTests
{
    static readonly ExtensionInstallCommand Install = new();
    static readonly ExtensionUninstallCommand Uninstall = new();
    static readonly ExtensionCancelUninstallCommand CancelUninstall = new();

    sealed class RecordingPolicy : IAuthorizationPolicy
    {
        public AuthorizationRequest? Asked;
        public AuthorizationMode Mode => AuthorizationMode.Confirm;
        public bool CanAsk => true;
        public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        {
            Asked = request;
            return Task.FromResult(AuthorizationDecision.Reject);
        }
    }

    static CommandResult Run(ICommand command, JsonObject args, IAuthorizationPolicy policy)
        => command.ExecuteAsync(CommandArgs.Parse(args.ToJsonString()), new CommandContext { Authorization = policy }, CancellationToken.None)
            .GetAwaiter().GetResult();

    [Fact]
    public void InstallingSomethingThatIsNotAPackageIsRefusedBeforeAsking()
    {
        var policy = new RecordingPolicy();
        var path = Path.Combine(Path.GetTempPath(), "tunelab-not-a-package-" + Path.GetRandomFileName() + ".zip");
        File.WriteAllText(path, "");

        var result = Run(Install, new JsonObject { ["path"] = path }, policy);

        Assert.True(result.IsError);
        Assert.Equal("not_a_package", result.Error!.Value.Code);
        Assert.Contains(".tlx", result.Error!.Value.Message);
        Assert.Null(policy.Asked);
    }

    [Fact]
    public void InstallingAMissingFileSaysSoWithoutAsking()
    {
        var policy = new RecordingPolicy();
        var result = Run(Install, new JsonObject { ["path"] = Path.Combine(Path.GetTempPath(), "definitely-not-here.tlx") }, policy);

        Assert.True(result.IsError);
        Assert.Equal("missing_file", result.Error!.Value.Code);
        Assert.Null(policy.Asked);
    }

    // 卸一个没装过的包：要指出去哪看装了什么，而不是只说"没有"。
    [Fact]
    public void UninstallingAnUnknownPackagePointsAtWhatIsInstalled()
    {
        var policy = new RecordingPolicy();
        var result = Run(Uninstall, new JsonObject { ["packageId"] = "com.nobody.nothing" }, policy);

        Assert.True(result.IsError);
        Assert.Equal("unknown_package", result.Error!.Value.Code);
        Assert.Contains("Installed:", result.Error!.Value.Message);
        Assert.Null(policy.Asked);
    }

    [Fact]
    public void CancellingAnUninstallOfAnUnknownPackageAlsoRefuses()
    {
        var policy = new RecordingPolicy();
        var result = Run(CancelUninstall, new JsonObject { ["packageId"] = "com.nobody.nothing" }, policy);

        Assert.True(result.IsError);
        Assert.Equal("unknown_package", result.Error!.Value.Code);
        Assert.Null(policy.Asked);
    }

    [Fact]
    public void EmptyPackageIdPointsAtTheList()
    {
        var policy = new RecordingPolicy();
        var result = Run(Uninstall, new JsonObject { ["packageId"] = "" }, policy);

        Assert.True(result.IsError);
        Assert.Equal("empty_id", result.Error!.Value.Code);
        Assert.Contains("list_extensions", result.Error!.Value.Message);
    }

    // ── 授权卡片上的那句话 ──
    // 用户看到的就这一句。它必须说出**后果**：装 = 把第三方代码放进来并当场运行；卸 = 重启时删文件；
    // 撤销卸载 = 留下它。三句话读起来必须是三件不同的事，否则那次点击等于没做选择。

    [Fact]
    public void InstallPhraseSaysItRunsThirdPartyCodeNow()
    {
        var phrase = new AuthorizationRequest(WriteKind.ExtensionInstall, 0, "SomeVoice", "C:/downloads/SomeVoice.tlx").ActionPhrase();

        Assert.Contains("install the extension \"SomeVoice\"", phrase);
        Assert.Contains("C:/downloads/SomeVoice.tlx", phrase);
        Assert.Contains("third-party code", phrase);
    }

    [Fact]
    public void UninstallPhraseSaysWhenTheFilesGo()
    {
        var uninstall = new AuthorizationRequest(WriteKind.ExtensionUninstall, 0, "SomeVoice", "uninstall").ActionPhrase();
        var keep = new AuthorizationRequest(WriteKind.ExtensionUninstall, 0, "SomeVoice", "keep").ActionPhrase();

        Assert.Contains("uninstall the extension \"SomeVoice\"", uninstall);
        Assert.Contains("next time TuneLab starts", uninstall);
        Assert.Contains("keep the extension \"SomeVoice\"", keep);
        Assert.NotEqual(uninstall, keep);
    }

    // `project open` 的卡片：用户必须看见"现在开着的那份会被关掉"，否则他以为只是多开一个。
    [Fact]
    public void OpeningAProjectPhraseSaysTheCurrentOneCloses()
    {
        var phrase = new AuthorizationRequest(WriteKind.ProjectOpen, 0, "C:/songs/b.tlpx").ActionPhrase();

        Assert.Contains("C:/songs/b.tlpx", phrase);
        Assert.Contains("closing the one the user has open", phrase);
        Assert.Contains("undo history", phrase);
    }
}
