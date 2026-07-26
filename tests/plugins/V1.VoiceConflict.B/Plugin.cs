using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.VoiceConflictB;

// 冲突消解夹具 B（voice）：引擎 id TLConflictVoice（与包 A 同身份、不同包 id）。
// 极简引擎：暴露一个声源，名字标注「Package B」——活实现是哪个包，Set Voice 菜单里的声源名即见分晓。
// 会话不产音频（合成静音）：路由/分组测试只需能区分活实现，不需要真实合成。
public sealed class ConflictVoiceEngine : IVoiceSynthesisEngine, IExtensionSettings
{
    // 扩展级设置：两个包用【同一个引擎 id】各声明一份设置 —— 供验证宿主/agent 在「同 id 跨包并存」时
    // 必须按 packageId 消歧（不许猜）。字段值标注包名，一眼看出读写落在哪个包的桶里。
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var props = new OrderedMap<PropertyKey, IControllerConfig>();
        props.Add(("variant", "Variant (Package B)"), TextBoxConfig.Create("b"));
        return ObjectConfig.Create(props);
    }

    public void ApplySettings(PropertyObject settings)
        => TuneLabContext.Global.GetLogger().Info(string.Format(
            "[VoiceConflict.B] ApplySettings: variant='{0}'", settings.GetString("variant", string.Empty)));

    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    public void Init()
        => mVoiceInfos.Add("conflict-voice", new VoiceSourceInfo { Name = "Conflict Voice (Package B)", Description = "From package B" });

    public void Destroy() { }

    public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context) => new SilentSession();

    // 声明（引擎层、纯函数、全空）：路由/分组测试不需要轨/面板。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => sEmptyAutomations;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => sEmptyAutomations;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => sEmptyConfig;
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => sEmptyConfig;
    public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context) => [];

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
    static readonly ObjectConfig sEmptyConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
    static readonly OrderedMap<PropertyKey, AutomationConfig> sEmptyAutomations = new();
}

// 静音会话：不调度任何段。够路由/分组测试用。
internal sealed class SilentSession : IVoiceSynthesisSession
{
    public string DefaultLyric => "la";
    // 本引擎无延音语义（每个 note 都是内容），如实恒 false——判定与合成行为成对，不做 melisma 就不声称。
    public bool IsContinuation(IVoiceSynthesisNote note) => false;

    public SynthesisRange? GetNextPendingSynthesisRange(double startTime, double endTime) => null;
    public Task SynthesizeNext(double startTime, double endTime, CancellationToken cancellation = default) => Task.CompletedTask;

    public SynthesizedPitch SynthesizedPitch => new() { Segments = [] };
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => sEmptyParameters;
    public IReadOnlyMap<string, SynthesizedSyllable> SynthesizedPhonemes => sEmptyPhonemes;

    public IReadOnlyList<SynthesisStatusSegment> Status => Array.Empty<SynthesisStatusSegment>();
    public IActionEvent SynthesizedPhonemesChanged => ActionEvent.Empty;
    public IActionEvent SynthesizedParametersChanged => ActionEvent.Empty;
    public IActionEvent SynthesizedPitchChanged => ActionEvent.Empty;
    public IActionEvent StatusChanged => ActionEvent.Empty;

    public void Dispose() { }

    static readonly ObjectConfig sEmptyConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
    static readonly OrderedMap<PropertyKey, AutomationConfig> sEmptyAutomations = new();
    static readonly Map<string, SynthesizedParameter> sEmptyParameters = new();
    static readonly Map<string, SynthesizedSyllable> sEmptyPhonemes = new();
}
