using System.Diagnostics;
using System.Text.Json;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1CustomWindow;

// 插件自己的设置界面演示（仅 Windows）：作者自己做一个界面 exe（../V1.CustomWindow.Editor，WinForms），插件本体不引任何
// UI 框架，设置页的按钮只负责启动它。界面是独立进程、与宿主无关——宿主不提供父窗口、不做模态；布局与数据
//（包目录下的 custom-window.json）全由 exe 与插件自己约定，不经过宿主的 config / 属性数据机制。
// 不参与实际合成（CreateSession 抛异常），仅借 voice 引擎的扩展设置页挂按钮。
public sealed class CustomWindowVoiceEngine : IVoiceSynthesisEngine, IExtensionSettings
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => sEmpty;

    public void Init() { }
    public void Destroy() { }

    public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context)
        => throw new NotSupportedException("V1.CustomWindow is a custom-window demo fixture.");

    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => sEmptyAutomations;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => sEmptyAutomations;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => sEmptyConfig;
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => sEmptyConfig;
    public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context) => [];

    // 两种启动方式：
    // · 等它关闭：回调一直等到 exe 退出——这期间按钮置灰（不会重复启动），exe 关掉后宿主重新询问本函数，
    //   按钮文字随 exe 存下的名字刷新；
    // · 只启动：回调立刻返回，exe 自己跑着。
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var props = new OrderedMap<PropertyKey, IControllerConfig>();
        props.Add(("editor", "Editor (wait until closed)"), ButtonConfig.Create("Open... (" + ReadName() + ")", _ => LaunchAndWait()));
        props.Add(("editor_detached", "Editor (detached)"), ButtonConfig.Create("Open...", _ =>
        {
            Launch().Dispose();
            return Task.CompletedTask;
        }));
        return ObjectConfig.Create(props);
    }

    public void ApplySettings(PropertyObject settings) { }

    static async Task LaunchAndWait()
    {
        using var process = Launch();
        await process.WaitForExitAsync();
        TuneLabContext.Global.GetLogger().Info("[V1.CustomWindow] editor exited with code " + process.ExitCode);
    }

    // 数据文件路径经命令行传给 exe（进程间约定由作者自定，这里最简单）。
    static Process Launch()
    {
        var start = new ProcessStartInfo(EditorPath) { UseShellExecute = false };
        start.ArgumentList.Add("--data");
        start.ArgumentList.Add(DataPath);
        return Process.Start(start) ?? throw new InvalidOperationException("Failed to start " + EditorPath);
    }

    static string ReadName()
    {
        try
        {
            if (!File.Exists(DataPath))
                return "default";
            using var json = JsonDocument.Parse(File.ReadAllText(DataPath));
            return json.RootElement.TryGetProperty("Name", out var name) ? name.GetString() ?? "default" : "default";
        }
        catch
        {
            return "default";
        }
    }

    static string PackageDir => Path.GetDirectoryName(typeof(CustomWindowVoiceEngine).Assembly.Location) ?? ".";
    static string EditorPath => Path.Combine(PackageDir, "editor", "V1.CustomWindow.Editor.exe");
    static string DataPath => Path.Combine(PackageDir, "custom-window.json");

    static readonly OrderedMap<string, VoiceSourceInfo> sEmpty = new();
    static readonly OrderedMap<PropertyKey, AutomationConfig> sEmptyAutomations = new();
    static readonly ObjectConfig sEmptyConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
}
