using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.Scripted;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// RemoteFormat 的宿主侧全链：假 sidecar 走真管道，验文件交接（导入落 stream_ref、导出读回输出流）
// 与 ProjectInfo wire 编解码在 RPC 两端的往返（设计 §5.4）。解释器缺席也覆盖。
[Collection("SidecarSpawns")]
    public class ScriptedRemoteFormatTests
{
    static RemoteFormat MakeProxy(string pkgId)
    {
        var dll = ScriptedSidecarHostTests.FakeDllPath;
        var host = SidecarRegistry.For(pkgId, Path.GetDirectoryName(dll)!, new RuntimeInfo(ScriptedSidecarHostTests.Dotnet, "dotnet-fake", "0.0", "x64"), Path.GetFileName(dll));
        host.ExpectFormat(Path.GetFileName(dll), ["tst"], []);
        return new RemoteFormat(host, Path.GetFileName(dll));
    }

    [Fact]
    public void ImportMaterializesProjectFromSyntheticReply()
    {
        var proxy = MakeProxy("com.test.rf-import");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("ignored by fake; content passes as stream_ref"));
        var project = ((IImportFormat)proxy).Deserialize(input);

        Assert.Single(project.Tracks);
        var part = Assert.IsType<MidiPartInfo>(project.Tracks[0].Parts[0]);
        var note = part.Notes[0];
        Assert.Equal(60, note.Pitch);
        Assert.Equal("la", note.Lyric);
        Assert.Equal(0.5, note.Properties.GetDouble("tension"));   // tagged {"n":0.5} 解回原生
        Assert.Equal("normal", note.Properties.GetString("mode"));
        Assert.Equal(SourceKind.Voice, part.SoundSource.Kind);
        SidecarRegistry.ShutdownAll();
    }

    [Fact]
    public void ExportRoundTripsProjectAndWritesBytes()
    {
        var proxy = MakeProxy("com.test.rf-export");
        var project = new ProjectInfo();
        project.Tracks.Add(new TrackInfo { Name = "T", Parts = { new MidiPartInfo { Name = "P" } } });

        using var output = new MemoryStream();
        ((IExportFormat)proxy).Serialize(output, project);
        // 假 sidecar 写死 FAKE-EXPORT；宿主把它读进自己拥有的输出流（stream 生命周期归宿主，见 IExportFormat 约定）。
        Assert.Equal("FAKE-EXPORT", Encoding.UTF8.GetString(output.ToArray()));
        SidecarRegistry.ShutdownAll();
    }

    [Fact]
    public void SettingsSchemaDecodesThroughConfigWire()
    {
        var dll = ScriptedSidecarHostTests.FakeDllPath;
        var host = SidecarRegistry.For("com.test.rf-settings", Path.GetDirectoryName(dll)!,
            new RuntimeInfo(ScriptedSidecarHostTests.Dotnet, "dotnet-fake", "0.0", "x64"), Path.GetFileName(dll));
        host.ExpectFormat(Path.GetFileName(dll), [], ["tst"]);
        var proxy = new RemoteFormat(host, Path.GetFileName(dll), hasSettings: true);

        var cfg = ((TuneLab.SDK.IExtensionSettings)proxy).GetSettingsConfig(new NullSettingsContext());
        var obj = Assert.IsType<ObjectConfig>(cfg);
        Assert.Equal(7, obj.Properties.Count);   // fake 的 WidgetConfig fixture 全量落地
        Assert.IsType<SliderConfig>(obj.Properties["gain"]);
        SidecarRegistry.ShutdownAll();
    }

    [Fact]
    public void UndeclaredSettingsYieldEmptyPanelAndNoRpc()
    {
        var dll = ScriptedSidecarHostTests.FakeDllPath;
        var host = SidecarRegistry.For("com.test.rf-nosettings", Path.GetDirectoryName(dll)!,
            new RuntimeInfo(ScriptedSidecarHostTests.Dotnet, "dotnet-fake", "0.0", "x64"), Path.GetFileName(dll));
        host.ExpectFormat(Path.GetFileName(dll), [], ["tst"]);
        var proxy = new RemoteFormat(host, Path.GetFileName(dll), hasSettings: false);

        var cfg = ((TuneLab.SDK.IExtensionSettings)proxy).GetSettingsConfig(new NullSettingsContext());
        Assert.Empty(((ObjectConfig)cfg).Properties);           // 未声明 = 空 schema，且从未 spawn
        Assert.False(host.Running);
    }

    sealed class NullSettingsContext : TuneLab.SDK.IExtensionSettingsContext
    {
        public TuneLab.Foundation.PropertyObject Settings => TuneLab.Foundation.PropertyObject.Empty;
    }
}
